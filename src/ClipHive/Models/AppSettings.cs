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
}

public enum AutoClearPolicy { TwoHours, ThreeDays, FifteenDays, OneMonth, Never }
