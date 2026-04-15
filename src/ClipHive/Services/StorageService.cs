using System.IO;
using Microsoft.Data.Sqlite;

namespace ClipHive;

/// <summary>
/// SQLite-backed clipboard history store.
/// Supports both text and image (JPEG bytes) clipboard items.
/// All content is encrypted at rest; decryption happens on retrieval.
/// A SemaphoreSlim(1,1) serialises all database access.
/// </summary>
public sealed class StorageService : IStorageService, IDisposable
{
    private readonly IEncryptionHelper _encryption;
    private readonly SqliteConnection _connection;
    private readonly System.Threading.SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    private static string GetDefaultConnectionString()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipHive");
        Directory.CreateDirectory(dir);
        return $"Data Source={Path.Combine(dir, "history.db")}";
    }

    public StorageService(IEncryptionHelper encryption, string? connectionString = null)
    {
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));

        string cs = connectionString is not null
            ? (connectionString == ":memory:" ? "Data Source=:memory:" : connectionString)
            : GetDefaultConnectionString();

        _connection = new SqliteConnection(cs);
        _connection.Open();

        using var wal = _connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        wal.ExecuteNonQuery();

        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS clipboard_items (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                ciphertext    TEXT    NOT NULL,
                iv            TEXT    NOT NULL,
                tag           TEXT    NOT NULL,
                created_at    TEXT    NOT NULL,
                source_app    TEXT,
                is_pinned     INTEGER NOT NULL DEFAULT 0,
                content_type  TEXT    NOT NULL DEFAULT 'text'
            );
            CREATE INDEX IF NOT EXISTS idx_created_at ON clipboard_items(created_at DESC);
            """;
        cmd.ExecuteNonQuery();

        // Migrate existing databases that lack the content_type column.
        try
        {
            using var alter = _connection.CreateCommand();
            alter.CommandText = "ALTER TABLE clipboard_items ADD COLUMN content_type TEXT NOT NULL DEFAULT 'text';";
            alter.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
        {
            // Column already exists — ignore.
        }

        // Migrate existing databases that lack the ocr_text column.
        try
        {
            using var alter = _connection.CreateCommand();
            alter.CommandText = "ALTER TABLE clipboard_items ADD COLUMN ocr_text TEXT;";
            alter.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
        {
            // Column already exists — ignore.
        }

        // Migrate existing databases that lack the content_hash column (used for deduplication).
        try
        {
            using var alter = _connection.CreateCommand();
            alter.CommandText = "ALTER TABLE clipboard_items ADD COLUMN content_hash TEXT;";
            alter.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
        {
            // Column already exists — ignore.
        }
    }

    /// <summary>Maximum non-pinned items retained. Updated when settings change.</summary>
    public int MaxHistoryCount { get; set; } = 500;

    /// <inheritdoc/>
    public async Task AddAsync(string plaintext, string? sourceApp = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ThrowIfDisposed();

        // SHA-256 of the plaintext — used to detect duplicates without decrypting all rows.
        string hash = ComputeHash(plaintext);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            // If the same content already exists, bump its timestamp to the top instead
            // of inserting a duplicate entry.
            using var check = _connection.CreateCommand();
            check.CommandText = """
                SELECT id FROM clipboard_items
                WHERE content_hash = $hash AND content_type = 'text'
                LIMIT 1;
                """;
            check.Parameters.AddWithValue("$hash", hash);
            var existingId = check.ExecuteScalar();

            if (existingId is not null)
            {
                using var bump = _connection.CreateCommand();
                bump.CommandText = "UPDATE clipboard_items SET created_at = $ca WHERE id = $id;";
                bump.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
                bump.Parameters.AddWithValue("$id", (long)existingId);
                bump.ExecuteNonQuery();
                return;
            }

            // New item — encrypt and insert.
            var (ciphertext, iv, tag) = _encryption.Encrypt(plaintext);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO clipboard_items (ciphertext, iv, tag, created_at, source_app, is_pinned, content_type, content_hash)
                VALUES ($ct, $iv, $tag, $ca, $sa, 0, 'text', $hash);
                """;
            cmd.Parameters.AddWithValue("$ct", ciphertext);
            cmd.Parameters.AddWithValue("$iv", iv);
            cmd.Parameters.AddWithValue("$tag", tag);
            cmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$sa", sourceApp is null ? DBNull.Value : (object)sourceApp);
            cmd.Parameters.AddWithValue("$hash", hash);
            cmd.ExecuteNonQuery();

            PurgeOverLimitUnlocked(MaxHistoryCount);
        }
        finally { _lock.Release(); }
    }

    private static string ComputeHash(string plaintext)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    /// <summary>
    /// Fast image fingerprint: hashes the byte-length plus three 128-byte samples
    /// (start / middle / end) rather than the entire image.
    /// O(1) cost regardless of image size; collision-resistant enough for clipboard dedup.
    /// </summary>
    private static string ComputeHashBytes(byte[] bytes)
    {
        const int Sample = 128;
        var buf = new byte[8 + Sample * 3];

        // Encode total length so images of different sizes never collide.
        BitConverter.GetBytes((long)bytes.Length).CopyTo(buf, 0);

        int n = Math.Min(Sample, bytes.Length);
        Array.Copy(bytes, 0, buf, 8, n);

        if (bytes.Length > Sample)
        {
            int mid = bytes.Length / 2;
            n = Math.Min(Sample, bytes.Length - mid);
            Array.Copy(bytes, mid, buf, 8 + Sample, n);

            int tail = Math.Max(0, bytes.Length - Sample);
            n = bytes.Length - tail;
            Array.Copy(bytes, tail, buf, 8 + Sample * 2, n);
        }

        return Convert.ToHexString(System.Security.Cryptography.MD5.HashData(buf));
    }

    /// <inheritdoc/>
    public async Task AddImageAsync(byte[] imageBytes, string? sourceApp = null, string? ocrText = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ThrowIfDisposed();

        // SHA-256 of the raw image bytes — same dedup strategy as text.
        // Without this, every WM_CLIPBOARDUPDATE re-fires for the same image
        // (e.g. on window open/close) and inserts a duplicate row.
        string hash = ComputeHashBytes(imageBytes);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            // If the same image is already stored, bump its timestamp to the top
            // instead of inserting a duplicate.
            using var check = _connection.CreateCommand();
            check.CommandText = """
                SELECT id FROM clipboard_items
                WHERE content_hash = $hash AND content_type = 'image'
                LIMIT 1;
                """;
            check.Parameters.AddWithValue("$hash", hash);
            var existingId = check.ExecuteScalar();

            if (existingId is not null)
            {
                using var bump = _connection.CreateCommand();
                bump.CommandText = "UPDATE clipboard_items SET created_at = $ca WHERE id = $id;";
                bump.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
                bump.Parameters.AddWithValue("$id", (long)existingId);
                bump.ExecuteNonQuery();
                return;
            }

            // New image — encrypt image bytes and OCR text separately, then insert.
            // OCR text must be encrypted to uphold the "all content encrypted at rest"
            // guarantee — storing it plaintext would expose image content without the key.
            string base64 = Convert.ToBase64String(imageBytes);
            var (ciphertext, iv, tag) = _encryption.Encrypt(base64);

            string? encryptedOcr = null;
            if (ocrText is not null)
            {
                var (ocrCt, ocrIv, ocrTag) = _encryption.Encrypt(ocrText);
                encryptedOcr = $"{ocrCt}:{ocrIv}:{ocrTag}";
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO clipboard_items (ciphertext, iv, tag, created_at, source_app, is_pinned, content_type, ocr_text, content_hash)
                VALUES ($ct, $iv, $tag, $ca, $sa, 0, 'image', $ocr, $hash);
                """;
            cmd.Parameters.AddWithValue("$ct", ciphertext);
            cmd.Parameters.AddWithValue("$iv", iv);
            cmd.Parameters.AddWithValue("$tag", tag);
            cmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$sa", sourceApp is null ? DBNull.Value : (object)sourceApp);
            cmd.Parameters.AddWithValue("$ocr", encryptedOcr is null ? DBNull.Value : (object)encryptedOcr);
            cmd.Parameters.AddWithValue("$hash", hash);
            cmd.ExecuteNonQuery();

            PurgeOverLimitUnlocked(MaxHistoryCount);
        }
        finally { _lock.Release(); }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ClipboardItem>> GetAllAsync()
    {
        ThrowIfDisposed();

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, ciphertext, iv, tag, created_at, source_app, is_pinned, content_type, ocr_text
                FROM clipboard_items
                ORDER BY is_pinned DESC, created_at DESC;
                """;
            return ReadItems(cmd);
        }
        finally { _lock.Release(); }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ClipboardItem>> SearchAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfDisposed();

        var all = await GetAllAsync().ConfigureAwait(false);
        return all.Where(item =>
            (item.ContentType == ClipboardContentType.Text &&
             item.EncryptedContent.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            (item.ContentType == ClipboardContentType.Image &&
             item.OcrText != null &&
             item.OcrText.Contains(query, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(long id)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM clipboard_items WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        finally { _lock.Release(); }
    }

    /// <inheritdoc/>
    public async Task DeleteAllAsync(bool keepPinned = true)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = keepPinned
                ? "DELETE FROM clipboard_items WHERE is_pinned = 0;"
                : "DELETE FROM clipboard_items;";
            cmd.ExecuteNonQuery();
        }
        finally { _lock.Release(); }
    }

    /// <inheritdoc/>
    public async Task DeleteOlderThanAsync(DateTime cutoff, bool keepPinned = true)
    {
        ThrowIfDisposed();
        string cutoffStr = cutoff.ToUniversalTime().ToString("O");
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = keepPinned
                ? "DELETE FROM clipboard_items WHERE created_at < $cutoff AND is_pinned = 0;"
                : "DELETE FROM clipboard_items WHERE created_at < $cutoff;";
            cmd.Parameters.AddWithValue("$cutoff", cutoffStr);
            cmd.ExecuteNonQuery();
        }
        finally { _lock.Release(); }
    }

    /// <inheritdoc/>
    public async Task SetPinnedAsync(long id, bool pinned)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE clipboard_items SET is_pinned = $pinned WHERE id = $id;";
            cmd.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        finally { _lock.Release(); }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private List<ClipboardItem> ReadItems(SqliteCommand cmd)
    {
        var items = new List<ClipboardItem>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            long id          = reader.GetInt64(0);
            string ciphertext = reader.GetString(1);
            string iv        = reader.GetString(2);
            string tag       = reader.GetString(3);
            DateTime createdAt = DateTime.Parse(reader.GetString(4), null,
                System.Globalization.DateTimeStyles.RoundtripKind);
            string? sourceApp = reader.IsDBNull(5) ? null : reader.GetString(5);
            bool isPinned    = reader.GetInt64(6) != 0;
            string contentTypeStr = reader.IsDBNull(7) ? "text" : reader.GetString(7);
            var contentType  = contentTypeStr == "image"
                ? ClipboardContentType.Image
                : ClipboardContentType.Text;
            string? ocrText  = null;
            if (reader.FieldCount > 8 && !reader.IsDBNull(8))
            {
                string raw = reader.GetString(8);
                // Format: "ciphertext:iv:tag" (encrypted) or plain text (legacy rows).
                var parts = raw.Split(':', 3);
                if (parts.Length == 3)
                {
                    try { ocrText = _encryption.Decrypt(parts[0], parts[1], parts[2]); }
                    catch { ocrText = null; } // corrupt encrypted ocr — skip gracefully
                }
                else
                {
                    // Legacy plaintext OCR from rows stored before encryption was added.
                    ocrText = raw;
                }
            }

            try
            {
                string decrypted = _encryption.Decrypt(ciphertext, iv, tag);

                if (contentType == ClipboardContentType.Image)
                {
                    // Decrypted value is the base64-encoded JPEG bytes.
                    byte[] imageData = Convert.FromBase64String(decrypted);
                    items.Add(new ClipboardItem(id, string.Empty, iv, tag,
                        createdAt, sourceApp, isPinned, ClipboardContentType.Image, imageData, ocrText));
                }
                else
                {
                    items.Add(new ClipboardItem(id, decrypted, iv, tag,
                        createdAt, sourceApp, isPinned));
                }
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException
                                          or FormatException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ClipHive] Skipping corrupt row id={id}: {ex.Message}");
            }
        }

        return items;
    }

    private void PurgeOverLimitUnlocked(int maxCount)
    {
        if (maxCount <= 0) return;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM clipboard_items
            WHERE is_pinned = 0
              AND id NOT IN (
                SELECT id FROM clipboard_items
                WHERE is_pinned = 0
                ORDER BY created_at DESC
                LIMIT $max
              );
            """;
        cmd.Parameters.AddWithValue("$max", maxCount);
        cmd.ExecuteNonQuery();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(StorageService));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Dispose();
            _connection.Dispose();
            _disposed = true;
        }
    }
}
