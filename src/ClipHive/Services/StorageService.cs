using System.IO;
using Microsoft.Data.Sqlite;

namespace ClipHive;

/// <summary>
/// SQLite-backed clipboard history store.
/// Supports both text and image (JPEG bytes) clipboard items.
/// All content is encrypted at rest; decryption happens on retrieval.
/// Dedupe fingerprints are keyed HMACs (never bare hashes of plaintext), so the
/// database leaks nothing that would let an attacker confirm content guesses offline.
/// A SemaphoreSlim(1,1) serialises all database access.
/// </summary>
public sealed class StorageService : IStorageService, IDisposable
{
    /// <summary>
    /// Schema/data version stored in PRAGMA user_version.
    /// v2: content_hash switched from plaintext SHA-256 (a guess-confirmation oracle)
    ///     to keyed HMAC-SHA256; image fingerprints now cover the full bytes; legacy
    ///     plaintext ocr_text rows re-encrypted.
    /// </summary>
    private const int CurrentDataVersion = 2;

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
        MigrateDataIfNeeded();
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

    /// <summary>
    /// One-time data migration to <see cref="CurrentDataVersion"/>. Pre-v2 rows carry a
    /// plaintext SHA-256 content_hash and possibly plaintext ocr_text; both are rewritten
    /// by decrypting each row and re-deriving the keyed fingerprint / encrypted OCR.
    /// Rows that fail to decrypt are left untouched (they are skipped on read anyway).
    /// </summary>
    private void MigrateDataIfNeeded()
    {
        using var versionCmd = _connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version;";
        long version = (long)(versionCmd.ExecuteScalar() ?? 0L);
        if (version >= CurrentDataVersion) return;

        using (var tx = _connection.BeginTransaction())
        {
            using var select = _connection.CreateCommand();
            select.Transaction = tx;
            select.CommandText = "SELECT id, ciphertext, iv, tag, content_type, ocr_text FROM clipboard_items;";

            var updates = new List<(long Id, string Hash, string? OcrEncrypted, bool UpdateOcr)>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    try
                    {
                        string decrypted = _encryption.Decrypt(
                            reader.GetString(1), reader.GetString(2), reader.GetString(3));
                        bool isImage = !reader.IsDBNull(4) && reader.GetString(4) == "image";

                        string hash = isImage
                            ? _encryption.ComputeContentHash(Convert.FromBase64String(decrypted))
                            : _encryption.ComputeContentHash(decrypted);

                        // Re-encrypt legacy plaintext OCR (v1.3.0 rows). Encrypted values
                        // are "ct:iv:tag" with base64 parts; anything else is plaintext.
                        string? ocrEncrypted = null;
                        bool updateOcr = false;
                        if (!reader.IsDBNull(5))
                        {
                            string rawOcr = reader.GetString(5);
                            if (!IsEncryptedOcr(rawOcr))
                            {
                                var (ct, iv, tag) = _encryption.Encrypt(rawOcr);
                                ocrEncrypted = $"{ct}:{iv}:{tag}";
                                updateOcr = true;
                            }
                        }

                        updates.Add((id, hash, ocrEncrypted, updateOcr));
                    }
                    catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException
                                                  or FormatException)
                    {
                        // Undecryptable row (wrong key / corrupt) — leave as-is.
                    }
                }
            }

            foreach (var (id, hash, ocrEncrypted, updateOcr) in updates)
            {
                using var update = _connection.CreateCommand();
                update.Transaction = tx;
                update.CommandText = updateOcr
                    ? "UPDATE clipboard_items SET content_hash = $hash, ocr_text = $ocr WHERE id = $id;"
                    : "UPDATE clipboard_items SET content_hash = $hash WHERE id = $id;";
                update.Parameters.AddWithValue("$hash", hash);
                update.Parameters.AddWithValue("$id", id);
                if (updateOcr)
                    update.Parameters.AddWithValue("$ocr", (object?)ocrEncrypted ?? DBNull.Value);
                update.ExecuteNonQuery();
            }

            using var stamp = _connection.CreateCommand();
            stamp.Transaction = tx;
            stamp.CommandText = $"PRAGMA user_version = {CurrentDataVersion};";
            stamp.ExecuteNonQuery();

            tx.Commit();
        }
    }

    /// <summary>
    /// True when an ocr_text value is in the encrypted "ct:iv:tag" format
    /// (three parts, each valid base64, 12-byte IV and 16-byte tag).
    /// </summary>
    private static bool IsEncryptedOcr(string raw)
    {
        var parts = raw.Split(':', 3);
        return parts.Length == 3
            && TryBase64Length(parts[0]) >= 0
            && TryBase64Length(parts[1]) == 12   // IV
            && TryBase64Length(parts[2]) == 16;  // GCM tag
    }

    private static int TryBase64Length(string s)
    {
        try { return Convert.FromBase64String(s).Length; }
        catch (FormatException) { return -1; }
    }

    /// <summary>Maximum non-pinned items retained. Updated when settings change.</summary>
    public int MaxHistoryCount { get; set; } = 500;

    /// <inheritdoc/>
    public async Task AddAsync(string plaintext, string? sourceApp = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ThrowIfDisposed();

        // Keyed HMAC of the plaintext — detects duplicates without decrypting all
        // rows, and (unlike a bare hash) reveals nothing about the content.
        string hash = _encryption.ComputeContentHash(plaintext);

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

    /// <inheritdoc/>
    public async Task AddImageAsync(byte[] imageBytes, string? sourceApp = null, string? ocrText = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ThrowIfDisposed();

        // Keyed HMAC over the FULL image bytes (they are already in memory, so a
        // sampled fingerprint would only save microseconds while risking silent
        // collisions between same-size screenshots).
        string hash = _encryption.ComputeContentHash(imageBytes);

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
                // Always the encrypted "ciphertext:iv:tag" format — legacy plaintext
                // rows were re-encrypted by the v2 migration, so plaintext values are
                // never accepted here (an attacker with DB write access must not be
                // able to plant readable rows).
                string raw = reader.GetString(8);
                var parts = raw.Split(':', 3);
                if (parts.Length == 3)
                {
                    try { ocrText = _encryption.Decrypt(parts[0], parts[1], parts[2]); }
                    catch { ocrText = null; } // corrupt encrypted ocr — skip gracefully
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
        if (_disposed) return;
        _disposed = true; // new operations now throw ObjectDisposedException

        // Give any in-flight operation a moment to finish before tearing down the
        // connection — fire-and-forget adds or an auto-clear tick may still hold
        // the semaphore during app shutdown.
        bool acquired = _lock.Wait(TimeSpan.FromSeconds(2));
        try
        {
            _connection.Dispose();
        }
        finally
        {
            if (acquired) _lock.Release();
            _lock.Dispose();
        }
    }
}
