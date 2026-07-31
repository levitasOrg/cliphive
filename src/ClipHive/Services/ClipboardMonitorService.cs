using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Clipboard = System.Windows.Clipboard;
using TextDataFormat = System.Windows.TextDataFormat;

namespace ClipHive;

/// <summary>
/// Monitors clipboard changes via Win32 <c>AddClipboardFormatListener</c>.
/// Fires <see cref="ClipboardChanged"/> for new Unicode text and
/// <see cref="ClipboardImageChanged"/> for new images/screenshots.
///
/// Never records:
/// <list type="bullet">
/// <item>ClipHive's own clipboard writes — every paste carries the private
/// <see cref="ClipboardFormats.OwnCopy"/> marker, so self-capture is impossible
/// regardless of timing (the <see cref="IPasteService.IsPasting"/> flag remains
/// as a cheap first-line check).</item>
/// <item>Content marked with the standard exclusion formats used by password
/// managers and the OS (<see cref="ClipboardFormats.ExcludeFromMonitoring"/>,
/// <see cref="ClipboardFormats.CanIncludeInHistory"/> = 0,
/// <see cref="ClipboardFormats.ClipboardViewerIgnore"/>).</item>
/// <item>Content matching the user's ignore lists (<see cref="Filter"/>).</item>
/// </list>
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // Win32 clipboard listener (HwndSource, WM_CLIPBOARDUPDATE) — desktop-only; filter logic lives in CaptureFilter, which is unit-tested
public sealed class ClipboardMonitorService : IClipboardMonitorService
{
    private readonly IPasteService _pasteService;

    private HwndSource? _hwndSource;
    private System.Windows.Threading.DispatcherTimer? _retryTimer;
    private bool _listening;
    private bool _disposed;

    public event EventHandler<ClipboardTextEventArgs>?  ClipboardChanged;
    public event EventHandler<ClipboardImageEventArgs>? ClipboardImageChanged;

    /// <summary>User-configured capture exclusions; updated by App on settings save.</summary>
    public CaptureFilter Filter { get; } = new();

    public ClipboardMonitorService(IPasteService pasteService)
    {
        ArgumentNullException.ThrowIfNull(pasteService);
        _pasteService = pasteService;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listening) return;

        var parameters = new HwndSourceParameters("ClipHive-ClipboardMonitor")
        {
            Width  = 0,
            Height = 0,
            WindowStyle         = 0,
            ExtendedWindowStyle = 0x00000080,
            ParentWindow        = IntPtr.Zero,
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
        Win32.AddClipboardFormatListener(_hwndSource.Handle);
        _listening = true;
    }

    public void Stop()
    {
        CancelRetry();
        if (!_listening || _hwndSource is null) return;

        Win32.RemoveClipboardFormatListener(_hwndSource.Handle);
        _hwndSource.RemoveHook(WndProc);
        _listening = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _hwndSource?.Dispose();
        _hwndSource = null;
    }

    // ── Window procedure ───────────────────────────────────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_CLIPBOARDUPDATE)
        {
            handled = true;
            OnClipboardUpdate();
        }
        return IntPtr.Zero;
    }

    private const int MaxContentChars = 100_000;

    private void OnClipboardUpdate()
    {
        try
        {
            TryCapture();
        }
        catch (Exception)
        {
            // Clipboard was locked by another process (CLIPBRD_E_CANT_OPEN) —
            // schedule a single retry of the FULL capture check, so the marker
            // and exclusion tests re-run against whatever is on the clipboard then.
            ScheduleRetry();
        }
    }

    private void TryCapture()
    {
        if (_disposed || _pasteService.IsPasting) return;
        if (IsExcludedFromCapture()) return;

        string? sourceApp = GetClipboardSourceApp();
        if (Filter.ShouldIgnoreApp(sourceApp)) return;

        // Images / screenshots take priority
        if (Clipboard.ContainsImage())
        {
            var imageBytes = GetClipboardImageBytes();
            if (imageBytes != null)
                ClipboardImageChanged?.Invoke(this, new ClipboardImageEventArgs(imageBytes, sourceApp));
            return;
        }

        if (!Clipboard.ContainsText(TextDataFormat.UnicodeText)) return;

        string text = Clipboard.GetText(TextDataFormat.UnicodeText);
        if (string.IsNullOrEmpty(text) || text.Length > MaxContentChars) return;
        if (Filter.ShouldIgnoreText(text)) return;

        ClipboardChanged?.Invoke(this, new ClipboardTextEventArgs(text, sourceApp));
    }

    // ── Exclusion checks ───────────────────────────────────────────────────────

    /// <summary>
    /// True when the current clipboard content must not be recorded: either ClipHive
    /// wrote it itself (own-copy marker), or the source app marked it with one of the
    /// standard "do not monitor" formats (password managers, Win+V opt-outs).
    /// </summary>
    private static bool IsExcludedFromCapture()
    {
        // Throws on clipboard contention — the caller's retry re-checks everything.
        var data = Clipboard.GetDataObject();
        if (data is null) return false;

        if (data.GetDataPresent(ClipboardFormats.OwnCopy) ||
            data.GetDataPresent(ClipboardFormats.ExcludeFromMonitoring) ||
            data.GetDataPresent(ClipboardFormats.ClipboardViewerIgnore))
            return true;

        // CanIncludeInClipboardHistory carries a DWORD: 0 = exclude.
        if (data.GetDataPresent(ClipboardFormats.CanIncludeInHistory) &&
            data.GetData(ClipboardFormats.CanIncludeInHistory) is MemoryStream ms)
        {
            Span<byte> dword = stackalloc byte[4];
            if (ms.Read(dword) == 4 && BitConverter.ToUInt32(dword) == 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Best-effort process name of the app that placed the data on the clipboard
    /// (via the clipboard-owner HWND). Null when the owner is unavailable, e.g.
    /// delayed-rendering or UWP broker scenarios.
    /// </summary>
    private static string? GetClipboardSourceApp()
    {
        try
        {
            IntPtr owner = Win32.GetClipboardOwner();
            if (owner == IntPtr.Zero) return null;

            Win32.GetWindowThreadProcessId(owner, out uint pid);
            if (pid == 0) return null;

            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null; // process exited or access denied — source is informational only
        }
    }

    // ── Retry ──────────────────────────────────────────────────────────────────

    private void ScheduleRetry()
    {
        CancelRetry();
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(_retryTimer, timer)) _retryTimer = null;
            try { TryCapture(); }
            catch (Exception) { /* clipboard still contended — give up */ }
        };
        _retryTimer = timer;
        timer.Start();
    }

    private void CancelRetry()
    {
        _retryTimer?.Stop();
        _retryTimer = null;
    }

    /// <summary>
    /// Reads the current clipboard image and encodes it as JPEG bytes.
    /// Returns null if the clipboard contains no image or the image is too large.
    /// </summary>
    private static byte[]? GetClipboardImageBytes()
    {
        var bitmapSource = Clipboard.GetImage();
        if (bitmapSource is null) return null;

        // Scale down if very large (max 1920 wide to keep storage manageable)
        const int MaxWidth = 1920;
        BitmapSource source = bitmapSource;
        if (source.PixelWidth > MaxWidth)
        {
            double scale = (double)MaxWidth / source.PixelWidth;
            source = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
        }

        using var ms = new MemoryStream();
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(ms);

        byte[] bytes = ms.ToArray();

        // Skip if > 5 MB
        const int MaxImageBytes = 5 * 1024 * 1024;
        return bytes.Length > MaxImageBytes ? null : bytes;
    }
}
