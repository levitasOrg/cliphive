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
    }

    /// <summary>Maximum non-pinned items retained. Updated when settings change.</summary>
    public int MaxHistoryCount { get; set; } = 500;

    /// <inheritdoc/>
    public async Task AddAsync(string plaintext, string? sourceApp = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ThrowIfDisposed();

        var (ciphertext, iv, tag) = _encryption.Encrypt(plaintext);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO clipboard_items (ciphertext, iv, tag, created_at, source_app, is_pinned, content_type)
                VALUES ($ct, $iv, $tag, $ca, $sa, 0, 'text');
                """;
            cmd.Parameters.AddWithValue("$ct", ciphertext);
            cmd.Parameters.AddWithValue("$iv", iv);
            cmd.Parameters.AddWithValue("$tag", tag);
            cmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$sa", sourceApp is null ? DBNull.Value : (object)sourceApp);
            cmd.ExecuteNonQuery();

            PurgeOverLimitUnlocked(MaxHistoryCount);
        }
        finally { _lock.Release(); }
    }

    /// <inheritdoc/>
    public async Task AddImageAsync(byte[] imageBytes, string? sourceApp = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ThrowIfDisposed();

        // Store image as base64-encoded string, then encrypt.
        string base64 = Convert.ToBase64String(imageBytes);
        var (ciphertext, iv, tag) = _encryption.Encrypt(base64);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO clipboard_items (ciphertext, iv, tag, created_at, source_app, is_pinned, content_type)
                VALUES ($ct, $iv, $tag, $ca, $sa, 0, 'image');
                """;
            cmd.Parameters.AddWithValue("$ct", ciphertext);
            cmd.Parameters.AddWithValue("$iv", iv);
            cmd.Parameters.AddWithValue("$tag", tag);
            cmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$sa", sourceApp is null ? DBNull.Value : (object)sourceApp);
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
                SELECT id, ciphertext, iv, tag, created_at, source_app, is_pinned, content_type
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
        return all
            .Where(item => item.ContentType == ClipboardContentType.Text &&
                           item.EncryptedContent.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
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

            try
            {
                string decrypted = _encryption.Decrypt(ciphertext, iv, tag);

                if (contentType == ClipboardContentType.Image)
                {
                    // Decrypted value is the base64-encoded JPEG bytes.
                    byte[] imageData = Convert.FromBase64String(decrypted);
                    items.Add(new ClipboardItem(id, string.Empty, iv, tag,
                        createdAt, sourceApp, isPinned, ClipboardContentType.Image, imageData));
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
