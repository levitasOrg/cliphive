namespace ClipHive;

/// <summary>
/// Manages system-wide hotkey registration via Win32 RegisterHotKey / UnregisterHotKey.
///
/// The service requires a window handle (HWND) that receives WM_HOTKEY messages.
/// In production this is the hidden HwndSource window.
/// </summary>
public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private const int HotkeyId = 9001; // arbitrary unique ID for ClipHive's hotkey

    private IntPtr _registeredHwnd = IntPtr.Zero;
    private bool _disposed;

    /// <summary>
    /// Raised when the registered hotkey is pressed.
    /// </summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>
    /// Registers the hotkey on the supplied window handle.
    /// If a hotkey was previously registered on a different handle it is first unregistered.
    /// </summary>
    /// <param name="hwnd">Handle of the message window.</param>
    /// <param name="modifiers">Modifier keys (MOD_CTRL, MOD_ALT, …).</param>
    /// <param name="virtualKey">Virtual key code.</param>
    /// <returns>True if registration succeeded.</returns>
    public bool Register(IntPtr hwnd, uint modifiers, uint virtualKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Unregister any previous registration
        if (_registeredHwnd != IntPtr.Zero)
            Unregister(_registeredHwnd);

        bool ok = Win32.RegisterHotKey(hwnd, HotkeyId, modifiers, virtualKey);
        if (ok)
            _registeredHwnd = hwnd;

        return ok;
    }

    /// <summary>
    /// Unregisters the hotkey on the given window handle.
    /// </summary>
    public void Unregister(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        Win32.UnregisterHotKey(hwnd, HotkeyId);
        if (_registeredHwnd == hwnd)
            _registeredHwnd = IntPtr.Zero;
    }

    /// <summary>
    /// Called by the WndProc to fire the <see cref="HotkeyPressed"/> event.
    /// </summary>
    public void OnWmHotkey(int id)
    {
        if (id == HotkeyId)
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
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
    }
}
