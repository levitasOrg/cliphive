using System.Runtime.InteropServices;

namespace ClipHive;

/// <summary>
/// All Win32 P/Invoke declarations used by ClipHive services.
/// No other file should contain DllImport or LibraryImport declarations.
/// </summary>
internal static class Win32
{
    // ── Hotkey modifier constants ──────────────────────────────────────────────
    public const uint MOD_ALT   = 0x0001;
    public const uint MOD_CTRL  = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN   = 0x0008;

    // ── Window message constants ───────────────────────────────────────────────
    public const int WM_HOTKEY         = 0x0312;
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    // ── Clipboard format ───────────────────────────────────────────────────────
    public const uint CF_UNICODETEXT = 13;

    // ── SendInput structures ───────────────────────────────────────────────────
    public const int INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        // MOUSEINPUT and HARDWAREINPUT omitted — not needed by ClipHive
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // ── P/Invoke: hotkeys ──────────────────────────────────────────────────────

    /// <summary>Registers a system-wide hotkey.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    /// <summary>Unregisters a previously registered system-wide hotkey.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ── P/Invoke: clipboard ────────────────────────────────────────────────────

    /// <summary>Adds the calling window to the clipboard format listener list.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    /// <summary>Removes the calling window from the clipboard format listener list.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    // ── P/Invoke: input ────────────────────────────────────────────────────────

    /// <summary>Synthesizes keystrokes, mouse motions, and button clicks.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ── P/Invoke: foreground window ───────────────────────────────────────────

    /// <summary>Returns the HWND of the foreground window.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>Sets the foreground window.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // ── DWM backdrop (Windows 11 Acrylic) ────────────────────────────────────

    /// <summary>DWM attribute: system backdrop type (Windows 11 Build 22000+).</summary>
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>Desktop Acrylic backdrop — suitable for transient popup windows.</summary>
    public const int DWMSBT_TRANSIENTWINDOW = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    /// <summary>Sets a DWM window attribute.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>Extends the DWM-drawn frame into the client area.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd, ref MARGINS pMarInset);
}
