using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClipHive.Tests.Unit;

/// <summary>
/// Security- and dedupe-focused StorageService tests: raw-database plaintext scans,
/// keyed fingerprints, the full image path, and the v2 data migration.
/// File-based SQLite is used where the test must inspect the database with an
/// independent connection.
/// </summary>
public sealed class StorageServiceSecurityTests : IDisposable
{
    private static readonly byte[] TestKey = new byte[32]
    {
        0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11,
        0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99,
        0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80,
        0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0, 0x01
    };

    private readonly EncryptionHelper _encryption = new(TestKey);
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"cliphive-test-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_dbPath}";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
        try { File.Delete(_dbPath + "-wal"); } catch (IOException) { }
        try { File.Delete(_dbPath + "-shm"); } catch (IOException) { }
    }

    // ── Raw database must not leak content ────────────────────────────────────

    [Fact]
    public async Task RawDatabase_ContainsNoPlaintext_AndNoBareContentHash()
    {
        const string secretText = "SuperSecretPassword-XYZZY";
        const string secretOcr  = "TopSecretOcrText-PLUGH";
        byte[] imageBytes = Encoding.UTF8.GetBytes("fake-jpeg-bytes-1234");

        using (var storage = new StorageService(_encryption, ConnectionString))
        {
            await storage.AddAsync(secretText);
            await storage.AddImageAsync(imageBytes, ocrText: secretOcr);
        }
        SqliteConnection.ClearAllPools();

        // Dump every stored value through an independent connection.
        var dump = new StringBuilder();
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM clipboard_items;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                for (int i = 0; i < reader.FieldCount; i++)
                    if (!reader.IsDBNull(i))
                        dump.Append(reader.GetValue(i)).Append('\n');
        }
        string raw = dump.ToString();

        Assert.DoesNotContain(secretText, raw);
        Assert.DoesNotContain(secretOcr, raw);
        Assert.DoesNotContain(Convert.ToBase64String(imageBytes), raw);

        // The dedupe fingerprint must not be a bare hash of the plaintext — that
        // would let anyone with the file confirm content guesses offline.
        string bareSha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(secretText)));
        Assert.DoesNotContain(bareSha256, raw, StringComparison.OrdinalIgnoreCase);
    }

    // ── Dedupe ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_SameTextTwice_SingleRowWithBumpedTimestamp()
    {
        using var storage = new StorageService(_encryption, ":memory:");

        await storage.AddAsync("dup me");
        var first = Assert.Single(await storage.GetAllAsync());

        await Task.Delay(20); // ensure a later timestamp
        await storage.AddAsync("dup me");

        var after = Assert.Single(await storage.GetAllAsync());
        Assert.Equal(first.Id, after.Id);
        Assert.True(after.CreatedAt > first.CreatedAt,
            "duplicate add should bump created_at to the top");
    }

    [Fact]
    public async Task AddImageAsync_SameBytesTwice_SingleRow()
    {
        using var storage = new StorageService(_encryption, ":memory:");
        byte[] image = Encoding.UTF8.GetBytes("same-image-bytes");

        await storage.AddImageAsync(image);
        await storage.AddImageAsync((byte[])image.Clone());

        Assert.Single(await storage.GetAllAsync());
    }

    [Fact]
    public async Task AddImageAsync_DistinctImagesWithSameLengthAndEdges_BothStored()
    {
        // Regression: the old sampled fingerprint (length + first/middle/last 128
        // bytes) collided for same-size images differing only elsewhere — the
        // second image was silently dropped. Full-content hashing must keep both.
        byte[] a = new byte[1024];
        byte[] b = new byte[1024];
        a[200] = 0x01; // differs at an offset the old sampler never read
        b[200] = 0x02;

        using var storage = new StorageService(_encryption, ":memory:");
        await storage.AddImageAsync(a);
        await storage.AddImageAsync(b);

        Assert.Equal(2, (await storage.GetAllAsync()).Count);
    }

    // ── Image path round-trip ─────────────────────────────────────────────────

    [Fact]
    public async Task AddImageAsync_RoundTrip_ReturnsBytesAndDecryptedOcr()
    {
        using var storage = new StorageService(_encryption, ":memory:");
        byte[] image = Encoding.UTF8.GetBytes("jpeg-payload");

        await storage.AddImageAsync(image, sourceApp: "SnippingTool",
            ocrText: "meeting at 12:30:45 room B"); // colons: regression for the
                                                    // legacy parser that nulled them

        var item = Assert.Single(await storage.GetAllAsync());
        Assert.Equal(ClipboardContentType.Image, item.ContentType);
        Assert.Equal(image, item.ImageData);
        Assert.Equal("SnippingTool", item.SourceApp);
        Assert.Equal("meeting at 12:30:45 room B", item.OcrText);
    }

    // ── v2 migration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Migration_RehashesRows_EncryptsLegacyOcr_AndStampsVersion()
    {
        const string legacyText = "legacy clipboard entry";
        const string legacyOcr  = "receipt total 12:30:45"; // colons on purpose
        byte[] legacyImage = Encoding.UTF8.GetBytes("legacy-image");

        // Build a pre-v2 database by hand: bare-SHA256 content_hash, plaintext ocr_text.
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using (var create = conn.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE clipboard_items (
                        id            INTEGER PRIMARY KEY AUTOINCREMENT,
                        ciphertext    TEXT    NOT NULL,
                        iv            TEXT    NOT NULL,
                        tag           TEXT    NOT NULL,
                        created_at    TEXT    NOT NULL,
                        source_app    TEXT,
                        is_pinned     INTEGER NOT NULL DEFAULT 0,
                        content_type  TEXT    NOT NULL DEFAULT 'text',
                        ocr_text      TEXT,
                        content_hash  TEXT
                    );
                    """;
                create.ExecuteNonQuery();
            }

            var (tc, ti, tt) = _encryption.Encrypt(legacyText);
            var (ic, ii, it) = _encryption.Encrypt(Convert.ToBase64String(legacyImage));

            using var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO clipboard_items (ciphertext, iv, tag, created_at, is_pinned, content_type, ocr_text, content_hash)
                VALUES ($tc, $ti, $tt, $ca1, 0, 'text', NULL, $th),
                       ($ic, $ii, $it, $ca2, 0, 'image', $ocr, $ih);
                """;
            insert.Parameters.AddWithValue("$tc", tc);
            insert.Parameters.AddWithValue("$ti", ti);
            insert.Parameters.AddWithValue("$tt", tt);
            insert.Parameters.AddWithValue("$ca1", DateTime.UtcNow.AddMinutes(-2).ToString("O"));
            insert.Parameters.AddWithValue("$th", Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(legacyText))));
            insert.Parameters.AddWithValue("$ic", ic);
            insert.Parameters.AddWithValue("$ii", ii);
            insert.Parameters.AddWithValue("$it", it);
            insert.Parameters.AddWithValue("$ca2", DateTime.UtcNow.AddMinutes(-1).ToString("O"));
            insert.Parameters.AddWithValue("$ocr", legacyOcr);
            insert.Parameters.AddWithValue("$ih", "OLD-SAMPLED-MD5-FINGERPRINT");
        insert.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        // Opening the store runs the migration.
        using (var storage = new StorageService(_encryption, ConnectionString))
        {
            // Legacy OCR (with colons) survives, now decrypted from its re-encrypted form.
            var items = await storage.GetAllAsync();
            var image = Assert.Single(items, i => i.ContentType == ClipboardContentType.Image);
            Assert.Equal(legacyOcr, image.OcrText);

            // Dedupe must recognise pre-migration rows under the new keyed fingerprint.
            await storage.AddAsync(legacyText);
            Assert.Single(await storage.GetAllAsync(),
                i => i.ContentType == ClipboardContentType.Text);

            // And the same for images (old sampled fingerprint replaced by full-bytes HMAC).
            await storage.AddImageAsync(legacyImage);
            Assert.Single(await storage.GetAllAsync(),
                i => i.ContentType == ClipboardContentType.Image);
        }
        SqliteConnection.ClearAllPools();

        // Raw post-migration checks: version stamped, no plaintext OCR, no bare hash.
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var version = conn.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(2L, (long)version.ExecuteScalar()!);

            using var dumpCmd = conn.CreateCommand();
            dumpCmd.CommandText = "SELECT ocr_text, content_hash FROM clipboard_items;";
            using var reader = dumpCmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    Assert.DoesNotContain(legacyOcr, reader.GetString(0));
                if (!reader.IsDBNull(1))
                {
                    Assert.NotEqual(Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(legacyText))), reader.GetString(1));
                    Assert.NotEqual("OLD-SAMPLED-MD5-FINGERPRINT", reader.GetString(1));
                }
            }
        }
    }

    [Fact]
    public void Migration_OnFreshDatabase_JustStampsVersion()
    {
        using (new StorageService(_encryption, ConnectionString)) { }
        SqliteConnection.ClearAllPools();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var version = conn.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, (long)version.ExecuteScalar()!);
    }
}
