using Xunit;

namespace ClipHive.Tests.Integration;

/// <summary>
/// Integration tests for StorageService + EncryptionHelper working together.
/// Uses in-memory SQLite to avoid file system side-effects.
/// </summary>
public sealed class StorageEncryptionIntegrationTests : IDisposable
{
    private static readonly byte[] TestKey = new byte[32]
    {
        0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
        0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00,
        0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10,
        0x0F, 0x1E, 0x2D, 0x3C, 0x4B, 0x5A, 0x69, 0x78
    };

    private readonly EncryptionHelper _encryption;
    private readonly StorageService _storage;

    public StorageEncryptionIntegrationTests()
    {
        _encryption = new EncryptionHelper(TestKey);
        _storage = new StorageService(_encryption, ":memory:");
    }

    public void Dispose()
    {
        _storage.Dispose();
    }

    [Fact]
    public async Task Write100Items_RetrieveAll_AllDecryptCorrectly()
    {
        const int count = 100;
        var expected = Enumerable.Range(1, count)
            .Select(i => $"Clipboard item number {i}: data_{i}")
            .ToList();

        foreach (string item in expected)
        {
            await _storage.AddAsync(item);
        }

        var retrieved = await _storage.GetAllAsync();

        Assert.Equal(count, retrieved.Count);

        // All retrieved content should match expected (order may vary — use set equality)
        var retrievedSet = retrieved.Select(x => x.EncryptedContent).ToHashSet();
        foreach (string e in expected)
        {
            Assert.Contains(e, retrievedSet);
        }
    }

    [Fact]
    public async Task Write100Items_SearchForSubset_ReturnsCorrectResults()
    {
        for (int i = 1; i <= 100; i++)
        {
            string content = i % 2 == 0 ? $"even-item-{i}" : $"odd-item-{i}";
            await _storage.AddAsync(content);
        }

        var evenResults = await _storage.SearchAsync("even-item");
        var oddResults = await _storage.SearchAsync("odd-item");

        Assert.Equal(50, evenResults.Count);
        Assert.Equal(50, oddResults.Count);
    }

    [Fact]
    public Task EncryptDecrypt_DifferentHelperInstances_SameKey_Succeeds()
    {
        // Two helpers with the same key should interoperate
        var helper1 = new EncryptionHelper(TestKey);
        var helper2 = new EncryptionHelper(TestKey);

        var (ciphertext, iv, tag) = helper1.Encrypt("cross-instance test");
        string decrypted = helper2.Decrypt(ciphertext, iv, tag);

        Assert.Equal("cross-instance test", decrypted);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WriteItems_DeleteOlderThan_VerifyRemainingDecryptCorrectly()
    {
        // Add 50 old items
        for (int i = 1; i <= 50; i++)
        {
            await _storage.AddAsync($"old-item-{i}");
        }

        DateTime cutoff = DateTime.UtcNow.AddMilliseconds(50);
        await Task.Delay(100); // ensure subsequent items are newer

        // Add 50 new items
        for (int i = 1; i <= 50; i++)
        {
            await _storage.AddAsync($"new-item-{i}");
        }

        await _storage.DeleteOlderThanAsync(cutoff, keepPinned: false);

        var remaining = await _storage.GetAllAsync();
        Assert.Equal(50, remaining.Count);
        Assert.All(remaining, item => Assert.StartsWith("new-item-", item.EncryptedContent));
    }

    [Fact]
    public async Task PinnedItems_SurviveDeleteAll()
    {
        // Add 10 items, pin 3 of them
        for (int i = 1; i <= 10; i++)
        {
            await _storage.AddAsync($"item-{i}");
        }

        var all = await _storage.GetAllAsync();
        // Pin the first 3
        for (int i = 0; i < 3; i++)
        {
            await _storage.SetPinnedAsync(all[i].Id, true);
        }

        await _storage.DeleteAllAsync(keepPinned: true);

        var remaining = await _storage.GetAllAsync();
        Assert.Equal(3, remaining.Count);
        Assert.All(remaining, item => Assert.True(item.IsPinned));
        // Verify they still decrypt correctly
        Assert.All(remaining, item => Assert.StartsWith("item-", item.EncryptedContent));
    }

    [Fact]
    public async Task MultipleSourceApps_FilteredCorrectly_AllDecryptOk()
    {
        await _storage.AddAsync("from notepad 1", "Notepad");
        await _storage.AddAsync("from notepad 2", "Notepad");
        await _storage.AddAsync("from vscode 1", "Code.exe");
        await _storage.AddAsync("from vscode 2", "Code.exe");
        await _storage.AddAsync("no source");

        var all = await _storage.GetAllAsync();
        Assert.Equal(5, all.Count);

        var notepadItems = all.Where(x => x.SourceApp == "Notepad").ToList();
        var vscodeItems = all.Where(x => x.SourceApp == "Code.exe").ToList();
        var noSourceItems = all.Where(x => x.SourceApp is null).ToList();

        Assert.Equal(2, notepadItems.Count);
        Assert.Equal(2, vscodeItems.Count);
        Assert.Single(noSourceItems);

        // Verify decrypt correctness
        Assert.All(notepadItems, item => Assert.StartsWith("from notepad", item.EncryptedContent));
        Assert.All(vscodeItems, item => Assert.StartsWith("from vscode", item.EncryptedContent));
    }

    [Fact]
    public async Task UnicodeContent_StoredAndRetrievedCorrectly()
    {
        string[] unicodeItems =
        [
            "こんにちは世界",
            "Привет мир 🌍",
            "مرحبا بالعالم",
            "🎉🚀💻🔐"
        ];

        foreach (string item in unicodeItems)
        {
            await _storage.AddAsync(item);
        }

        var retrieved = await _storage.GetAllAsync();
        Assert.Equal(unicodeItems.Length, retrieved.Count);

        var retrievedSet = retrieved.Select(x => x.EncryptedContent).ToHashSet();
        foreach (string expected in unicodeItems)
        {
            Assert.Contains(expected, retrievedSet);
        }
    }
}
