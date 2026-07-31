// src/ClipHive/Contracts.cs

namespace ClipHive;

/// <summary>
/// Clipboard format names shared between the paste and monitor services.
/// </summary>
public static class ClipboardFormats
{
    /// <summary>
    /// Private format stamped on every clipboard write ClipHive itself performs.
    /// The monitor skips updates carrying this marker, so pasting an item back
    /// never re-captures or reorders history (deterministic, unlike a timing flag).
    /// </summary>
    public const string OwnCopy = "ClipHive.OwnCopy";

    /// <summary>
    /// Standard Windows format set by password managers (KeePass, Bitwarden, …)
    /// and the OS itself to mark clipboard content that monitors must not record.
    /// Presence alone means "do not capture".
    /// </summary>
    public const string ExcludeFromMonitoring = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>
    /// Standard format carrying a DWORD; 0 means the content must not be added
    /// to clipboard history (Win+V convention, honored by ClipHive too).
    /// </summary>
    public const string CanIncludeInHistory = "CanIncludeInClipboardHistory";

    /// <summary>
    /// Legacy convention (Ditto/ClipboardFusion era) — presence means "ignore".
    /// </summary>
    public const string ClipboardViewerIgnore = "Clipboard Viewer Ignore";
}

public sealed class ClipboardTextEventArgs : EventArgs
{
    public ClipboardTextEventArgs(string text, string? sourceApp)
    {
        Text = text;
        SourceApp = sourceApp;
    }

    public string Text { get; }
    public string? SourceApp { get; }
}

public sealed class ClipboardImageEventArgs : EventArgs
{
    public ClipboardImageEventArgs(byte[] imageBytes, string? sourceApp)
    {
        ImageBytes = imageBytes;
        SourceApp = sourceApp;
    }

    public byte[] ImageBytes { get; }
    public string? SourceApp { get; }
}

public interface IStorageService
{
    Task AddAsync(string plaintext, string? sourceApp = null);
    Task AddImageAsync(byte[] imageBytes, string? sourceApp = null, string? ocrText = null);
    Task<IReadOnlyList<ClipboardItem>> GetAllAsync();
    Task<IReadOnlyList<ClipboardItem>> SearchAsync(string query);
    Task DeleteAsync(long id);
    Task DeleteAllAsync(bool keepPinned = true);
    Task DeleteOlderThanAsync(DateTime cutoff, bool keepPinned = true);
    Task SetPinnedAsync(long id, bool pinned);
}

public interface IEncryptionHelper
{
    (string Ciphertext, string Iv, string Tag) Encrypt(string plaintext);
    string Decrypt(string ciphertext, string iv, string tag);

    /// <summary>
    /// Keyed dedupe fingerprint (HMAC-SHA256 under a key derived from the encryption
    /// key). Unlike a bare hash, the stored value is useless for offline
    /// guess-confirmation of clipboard contents without the DPAPI-protected key.
    /// </summary>
    string ComputeContentHash(string plaintext);

    /// <inheritdoc cref="ComputeContentHash(string)"/>
    string ComputeContentHash(byte[] bytes);
}

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public interface IHotkeyService
{
    event EventHandler? HotkeyPressed;
    event EventHandler? PlainTextHotkeyPressed;
    bool Register(IntPtr hwnd, uint modifiers, uint virtualKey);
    bool RegisterPlainText(IntPtr hwnd, uint modifiers, uint virtualKey);
    void Unregister(IntPtr hwnd);
}

public interface IPasteService
{
    bool IsPasting { get; }
    Task PasteAsync(string content);
    Task PasteImageAsync(byte[] imageBytes);
    Task PastePlainTextFromClipboardAsync();
}

public interface IClipboardMonitorService : IDisposable
{
    event EventHandler<ClipboardTextEventArgs>? ClipboardChanged;
    event EventHandler<ClipboardImageEventArgs>? ClipboardImageChanged;
    void Start();
    void Stop();
}

public interface IAutoClearService : IDisposable
{
    /// <summary>Raised after a cleanup pass actually ran (policy != Never).</summary>
    event EventHandler? Cleaned;
    void Start();
    void Stop();
}
