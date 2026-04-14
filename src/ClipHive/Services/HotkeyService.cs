namespace ClipHive;

/// <summary>
/// Manages system-wide hotkey registration via Win32 RegisterHotKey / UnregisterHotKey.
///
/// The service requires a window handle (HWND) that receives WM_HOTKEY messages.
/// In production this is the hidden HwndSource window.
/// </summary>
public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private const int HotkeyId          = 9001; // main sidebar hotkey
    private const int PlainTextHotkeyId = 9002; // paste-as-plain-text hotkey

    private IntPtr _registeredHwnd      = IntPtr.Zero;
    private IntPtr _plainTextHwnd       = IntPtr.Zero;
    private bool _disposed;

    /// <summary>Raised when the main sidebar hotkey is pressed.</summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>Raised when the plain-text paste hotkey is pressed.</summary>
    public event EventHandler? PlainTextHotkeyPressed;

    /// <summary>
    /// Registers the main sidebar hotkey on the supplied window handle.
    /// If a hotkey was previously registered on a different handle it is first unregistered.
    /// </summary>
    public bool Register(IntPtr hwnd, uint modifiers, uint virtualKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_registeredHwnd != IntPtr.Zero)
            Unregister(_registeredHwnd);

        bool ok = Win32.RegisterHotKey(hwnd, HotkeyId, modifiers, virtualKey);
        if (ok)
            _registeredHwnd = hwnd;

        return ok;
    }

    /// <summary>
    /// Registers the plain-text paste hotkey (Ctrl+Alt+V by default).
    /// </summary>
    public bool RegisterPlainText(IntPtr hwnd, uint modifiers, uint virtualKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_plainTextHwnd != IntPtr.Zero)
            Win32.UnregisterHotKey(_plainTextHwnd, PlainTextHotkeyId);

        bool ok = Win32.RegisterHotKey(hwnd, PlainTextHotkeyId, modifiers, virtualKey);
        if (ok)
            _plainTextHwnd = hwnd;

        return ok;
    }

    /// <summary>Unregisters the main hotkey on the given window handle.</summary>
    public void Unregister(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        Win32.UnregisterHotKey(hwnd, HotkeyId);
        if (_registeredHwnd == hwnd)
            _registeredHwnd = IntPtr.Zero;
    }

    /// <summary>Called by the WndProc to fire the appropriate hotkey event.</summary>
    public void OnWmHotkey(int id)
    {
        if (id == HotkeyId)
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        else if (id == PlainTextHotkeyId)
            PlainTextHotkeyPressed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registeredHwnd != IntPtr.Zero)
        {
            Win32.UnregisterHotKey(_registeredHwnd, HotkeyId);
            _registeredHwnd = IntPtr.Zero;
        }

        if (_plainTextHwnd != IntPtr.Zero)
        {
            Win32.UnregisterHotKey(_plainTextHwnd, PlainTextHotkeyId);
            _plainTextHwnd = IntPtr.Zero;
        }
    }
}
