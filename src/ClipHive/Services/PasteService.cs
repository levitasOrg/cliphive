using System.Windows;
using System.Windows.Media.Imaging;
using Clipboard = System.Windows.Clipboard;
using TextDataFormat = System.Windows.TextDataFormat;

namespace ClipHive;

/// <summary>
/// Pastes text or image content into the previously focused window by:
/// 1. Writing the content to the clipboard.
/// 2. Sending Ctrl+V keystrokes to the target application.
///
/// <see cref="IsPasting"/> is set before writing and cleared after, so
/// <see cref="ClipboardMonitorService"/> can suppress the self-generated event.
/// </summary>
public sealed class PasteService : IPasteService
{
    private int _isPastingInt; // 0 = idle, 1 = pasting

    public bool IsPasting => System.Threading.Volatile.Read(ref _isPastingInt) == 1;

    /// <inheritdoc/>
    public async Task PasteAsync(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (System.Threading.Interlocked.CompareExchange(ref _isPastingInt, 1, 0) != 0)
            return;

        try
        {
            // Clipboard.SetText must be called on an STA thread — marshal to the
            // WPF UI dispatcher so this works even when the continuation lands on
            // the thread pool after a ConfigureAwait(false).
            var app = System.Windows.Application.Current;
            if (app != null)
                await app.Dispatcher.InvokeAsync(() =>
                    Clipboard.SetText(content, TextDataFormat.UnicodeText));
            else
                Clipboard.SetText(content, TextDataFormat.UnicodeText);

            await Task.Delay(50).ConfigureAwait(false);
            SendCtrlV();
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _isPastingInt, 0);
        }
    }

    /// <inheritdoc/>
    public async Task PasteImageAsync(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        if (System.Threading.Interlocked.CompareExchange(ref _isPastingInt, 1, 0) != 0)
            return;

        try
        {
            var bitmapSource = LoadBitmapSource(imageBytes);

            // Clipboard.SetImage must be called on an STA thread — marshal to the
            // WPF UI dispatcher so this works even when the continuation lands on
            // the thread pool after a ConfigureAwait(false).
            var app = System.Windows.Application.Current;
            if (app != null)
                await app.Dispatcher.InvokeAsync(() => Clipboard.SetImage(bitmapSource));
            else
                Clipboard.SetImage(bitmapSource);

            await Task.Delay(50).ConfigureAwait(false);
            SendCtrlV();
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _isPastingInt, 0);
        }
    }

    /// <inheritdoc/>
    public async Task PastePlainTextFromClipboardAsync()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _isPastingInt, 1, 0) != 0)
            return;

        try
        {
            // Retrieve the plain-text format only (strips RTF/HTML rich formatting).
            var app = System.Windows.Application.Current;
            if (app != null)
                await app.Dispatcher.InvokeAsync(() =>
                {
                    string plain = Clipboard.ContainsText(TextDataFormat.Text)
                        ? Clipboard.GetText(TextDataFormat.Text)
                        : string.Empty;
                    if (!string.IsNullOrEmpty(plain))
                        Clipboard.SetText(plain, TextDataFormat.UnicodeText);
                });
            else
            {
                string plain = Clipboard.GetText(TextDataFormat.Text);
                if (!string.IsNullOrEmpty(plain))
                    Clipboard.SetText(plain, TextDataFormat.UnicodeText);
            }

            await Task.Delay(50).ConfigureAwait(false);
            SendCtrlV();
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _isPastingInt, 0);
        }
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private static BitmapSource LoadBitmapSource(byte[] imageBytes)
    {
        var bmp = new BitmapImage();
        using var ms = new System.IO.MemoryStream(imageBytes);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static void SendCtrlV()
    {
        const ushort VK_CONTROL = 0x11;
        const ushort VK_V       = 0x56;

        var inputs = new Win32.INPUT[]
        {
            new() { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = VK_CONTROL } } },
            new() { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = VK_V } } },
            new() { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = VK_V,       dwFlags = Win32.KEYEVENTF_KEYUP } } },
            new() { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = VK_CONTROL, dwFlags = Win32.KEYEVENTF_KEYUP } } },
        };

        Win32.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Win32.INPUT>());
    }
}
