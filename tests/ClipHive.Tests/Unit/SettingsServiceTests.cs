using System.IO;
using Xunit;

namespace ClipHive.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SettingsService"/>.
/// Uses a temporary directory so tests are isolated and do not touch the real
/// %LOCALAPPDATA%\ClipHive folder.
/// </summary>
public class SettingsServiceTests : IDisposable
{
    // ── Test fixture ───────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly SettingsService _svc;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ClipHiveTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        // Use the internal constructor to write to a temp directory
        _svc = new SettingsService(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Load ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_FileMissing_ReturnsDefaults()
    {
        AppSettings result = _svc.Load();

        Assert.NotNull(result);
        Assert.Equal(AutoClearPolicy.Never, result.AutoClear);
        Assert.False(result.StartWithWindows);
        Assert.Equal(500, result.MaxHistoryCount);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(_svc.ConfigPath, "{ NOT VALID JSON }}}");

        AppSettings result = _svc.Load();

        Assert.NotNull(result);
        Assert.Equal(AutoClearPolicy.Never, result.AutoClear);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefaults()
    {
        File.WriteAllText(_svc.ConfigPath, string.Empty);

        AppSettings result = _svc.Load();

        Assert.NotNull(result);
    }

    // ── Save + Load round-trip ─────────────────────────────────────────────────

    [Fact]
    public void SaveLoad_RoundTrip_AllProperties()
    {
        var original = new AppSettings
        {
            HotkeyModifiers   = 0x0002 | 0x0001, // MOD_CTRL | MOD_ALT
            HotkeyVirtualKey  = 0x48,             // 'H'
            AutoClear         = AutoClearPolicy.ThreeDays,
            StartWithWindows  = true,
            MaxHistoryCount   = 250,
        };

        _svc.Save(original);
        AppSettings loaded = _svc.Load();

        Assert.Equal(original.HotkeyModifiers,  loaded.HotkeyModifiers);
        Assert.Equal(original.HotkeyVirtualKey, loaded.HotkeyVirtualKey);
        Assert.Equal(original.AutoClear,         loaded.AutoClear);
        Assert.Equal(original.StartWithWindows,  loaded.StartWithWindows);
        Assert.Equal(original.MaxHistoryCount,   loaded.MaxHistoryCount);
    }

    [Theory]
    [InlineData(AutoClearPolicy.TwoHours)]
    [InlineData(AutoClearPolicy.ThreeDays)]
    [InlineData(AutoClearPolicy.FifteenDays)]
    [InlineData(AutoClearPolicy.OneMonth)]
    [InlineData(AutoClearPolicy.Never)]
    public void SaveLoad_AllAutoClearPolicies(AutoClearPolicy policy)
    {
        var settings = new AppSettings { AutoClear = policy };
        _svc.Save(settings);

        AppSettings loaded = _svc.Load();
        Assert.Equal(policy, loaded.AutoClear);
    }

    [Fact]
    public void Save_ThenSaveAgain_OverwritesPrevious()
    {
        _svc.Save(new AppSettings { MaxHistoryCount = 100 });
        _svc.Save(new AppSettings { MaxHistoryCount = 999 });

        AppSettings loaded = _svc.Load();
        Assert.Equal(999, loaded.MaxHistoryCount);
    }

    // ── Atomicity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesJsonFile_NotBinaryOrEmpty()
    {
        _svc.Save(new AppSettings());

        string content = File.ReadAllText(_svc.ConfigPath);
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Contains("{", content);  // must be JSON-like
    }

    [Fact]
    public void Save_NoTmpFileLeft_AfterSuccessfulSave()
    {
        _svc.Save(new AppSettings());

        string tmpPath = Path.Combine(_tempDir, "settings.json.tmp");
        Assert.False(File.Exists(tmpPath), "Temporary file should be removed after save.");
    }

    // ── Save argument validation ───────────────────────────────────────────────

    [Fact]
    public void Save_NullSettings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _svc.Save(null!));
    }

    // ── Default constructor path check ─────────────────────────────────────────

    [Fact]
    public void DefaultConstructor_UsesLocalAppDataPath()
    {
        var defaultSvc = new SettingsService();
        string expectedDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipHive");
        string expectedPath = Path.Combine(expectedDir, "settings.json");

        Assert.Equal(expectedPath, defaultSvc.ConfigPath);
    }
}
