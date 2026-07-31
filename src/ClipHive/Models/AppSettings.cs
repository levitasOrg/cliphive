namespace ClipHive;

public sealed class AppSettings
{
    // MOD_CTRL = 0x0002, MOD_SHIFT = 0x0004
    public uint HotkeyModifiers { get; set; } = 0x0002 | 0x0004;
    public uint HotkeyVirtualKey { get; set; } = 0x56; // 'V'
    // MOD_CTRL = 0x0002, MOD_ALT = 0x0001 → Ctrl+Alt+V
    public uint PlainTextHotkeyModifiers { get; set; } = 0x0002 | 0x0001;
    public uint PlainTextHotkeyVirtualKey { get; set; } = 0x56; // 'V'
    public AutoClearPolicy AutoClear { get; set; } = AutoClearPolicy.Never;
    public bool StartWithWindows { get; set; } = false;
    public int MaxHistoryCount { get; set; } = 500;
    public bool HideFromTray { get; set; } = false;

    /// <summary>
    /// Process names (without .exe) whose copies are never recorded,
    /// e.g. ["KeePass", "Bitwarden"]. Case-insensitive. Edited in settings.json.
    /// Note: apps following the standard exclusion clipboard formats are always
    /// ignored automatically, without needing an entry here.
    /// </summary>
    public List<string> IgnoredApps { get; set; } = new();

    /// <summary>
    /// Regex patterns; captured text matching any of them is never recorded,
    /// e.g. ["^ghp_[A-Za-z0-9]+$"]. Case-insensitive. Edited in settings.json.
    /// </summary>
    public List<string> IgnorePatterns { get; set; } = new();
}

public enum AutoClearPolicy { TwoHours, ThreeDays, FifteenDays, OneMonth, Never }
