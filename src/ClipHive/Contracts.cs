// src/ClipHive/Contracts.cs

namespace ClipHive;

public interface IStorageService
{
    Task AddAsync(string plaintext, string? sourceApp = null);
    Task AddImageAsync(byte[] imageBytes, string? sourceApp = null);
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
}

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public interface IHotkeyService
{
    event EventHandler? HotkeyPressed;
    bool Register(IntPtr hwnd, uint modifiers, uint virtualKey);
    void Unregister(IntPtr hwnd);
}

public interface IPasteService
{
    bool IsPasting { get; }
    Task PasteAsync(string content);
    Task PasteImageAsync(byte[] imageBytes);
}

public interface IClipboardMonitorService : IDisposable
{
    event EventHandler<string>? ClipboardChanged;
    event EventHandler<byte[]>? ClipboardImageChanged;
    void Start();
    void Stop();
}

public interface IAutoClearService : IDisposable
{
    void Start();
    void Stop();
}
