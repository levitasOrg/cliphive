using System.Windows;
using System.Windows.Media.Imaging;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;
using TextDataFormat = System.Windows.TextDataFormat;

namespace ClipHive;

/// <summary>
/// Pastes text or image content into the previously focused window by:
/// 1. Writing the content to the clipboard, stamped with the private
///    <see cref="ClipboardFormats.OwnCopy"/> marker so the clipboard monitor
///    deterministically ignores the self-generated update (no timing window).
/// 2. Sending Ctrl+V keystrokes to the target application.
///
/// <see cref="IsPasting"/> remains as a cheap first-line suppression check.
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
            // Clipboard writes must happen on an STA thread — marshal to the
            // WPF UI dispatcher so this works even when the continuation lands on
            // the thread pool after a ConfigureAwait(false).
            await RunOnStaThread(() => SetClipboardMarked(d =>
                d.SetData(System.Windows.DataFormats.UnicodeText, content)));

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

            await RunOnStaThread(() => SetClipboardMarked(d => d.SetImage(bitmapSource)));

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
            await RunOnStaThread(() =>
            {
                // UnicodeText is already plain: rewriting the clipboard with ONLY this
                // format is what strips RTF/HTML. (Never round-trip through the ANSI
                // TextDataFormat.Text — that destroys any non-codepage character:
                // CJK, Cyrillic, emoji all become '?'.)
                string plain = Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    ? Clipboard.GetText(TextDataFormat.UnicodeText)
                    : string.Empty;
                if (!string.IsNullOrEmpty(plain))
                    SetClipboardMarked(d => d.SetData(System.Windows.DataFormats.UnicodeText, plain));
            });

            await Task.Delay(50).ConfigureAwait(false);
            SendCtrlV();
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _isPastingInt, 0);
        }
    }

    // ── Private ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a DataObject to the clipboard carrying <paramref name="populate"/>'s
    /// content plus the own-copy marker. copy:true so the data survives app exit.
    /// </summary>
    private static void SetClipboardMarked(Action<DataObject> populate)
    {
        var data = new DataObject();
        populate(data);
        data.SetData(ClipboardFormats.OwnCopy, new byte[] { 1 });
        Clipboard.SetDataObject(data, copy: true);
    }

    private static async Task RunOnStaThread(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app != null)
            await app.Dispatcher.InvokeAsync(action);
        else
            action();
    }

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
