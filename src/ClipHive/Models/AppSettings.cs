namespace ClipHive;

public sealed class AppSettings
{
    // MOD_CTRL = 0x0002, MOD_SHIFT = 0x0004
    public uint HotkeyModifiers { get; set; } = 0x0002 | 0x0004;
    public uint HotkeyVirtualKey { get; set; } = 0x56; // 'V'
    public AutoClearPolicy AutoClear { get; set; } = AutoClearPolicy.Never;
    public bool StartWithWindows { get; set; } = false;
    public int MaxHistoryCount { get; set; } = 500;
}

public enum AutoClearPolicy { TwoHours, ThreeDays, FifteenDays, OneMonth, Never }
