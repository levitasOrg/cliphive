using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClipHive;

/// <summary>
/// Thrown when the persisted encryption key cannot be loaded or created.
/// The app offers the user a key reset (which discards the encrypted history,
/// unreadable without the key) instead of crashing on every launch.
/// </summary>
public sealed class EncryptionKeyException : Exception
{
    public EncryptionKeyException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// AES-256-GCM encryption helper with optional DPAPI key derivation.
/// Implements IEncryptionHelper for dependency injection.
/// </summary>
public sealed class EncryptionHelper : IEncryptionHelper
{
    private readonly byte[] _key;
    private readonly byte[] _hashKey;
    private const int IvSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256-bit

    /// <summary>
    /// Constructs an EncryptionHelper.
    /// If <paramref name="key"/> is null, loads/creates the persistent key via DPAPI
    /// (user-bound, CurrentUser scope). If provided, uses the supplied key directly
    /// (useful for testing).
    /// </summary>
    public EncryptionHelper(byte[]? key = null)
    {
        if (key is not null)
        {
            if (key.Length != KeySize)
                throw new ArgumentException($"Key must be {KeySize} bytes.", nameof(key));
            _key = key;
        }
        else
        {
            _key = DeriveKey();
        }

        // Separate key for content fingerprints so the AES key is never used
        // directly as a MAC key. Derived, not stored: HMAC(_key, fixed label).
        using var kdf = new HMACSHA256(_key);
        _hashKey = kdf.ComputeHash(Encoding.UTF8.GetBytes("ClipHive.ContentHash.v2"));
    }

    /// <inheritdoc />
    public (string Ciphertext, string Iv, string Tag) Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] iv = RandomNumberGenerator.GetBytes(IvSize);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[TagSize];

        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Encrypt(iv, plaintextBytes, ciphertext, tag);

        return (
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(iv),
            Convert.ToBase64String(tag)
        );
    }

    /// <inheritdoc />
    public string Decrypt(string ciphertext, string iv, string tag)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(iv);
        ArgumentNullException.ThrowIfNull(tag);

        byte[] ciphertextBytes = Convert.FromBase64String(ciphertext);
        byte[] ivBytes = Convert.FromBase64String(iv);
        byte[] tagBytes = Convert.FromBase64String(tag);
        byte[] plaintextBytes = new byte[ciphertextBytes.Length];

        using var aesGcm = new AesGcm(_key, TagSize);
        // AesGcm.Decrypt throws CryptographicException on auth tag mismatch — do NOT swallow
        aesGcm.Decrypt(ivBytes, ciphertextBytes, tagBytes, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    /// <inheritdoc />
    public string ComputeContentHash(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return ComputeContentHash(Encoding.UTF8.GetBytes(plaintext));
    }

    /// <inheritdoc />
    public string ComputeContentHash(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var hmac = new HMACSHA256(_hashKey);
        return Convert.ToHexString(hmac.ComputeHash(bytes));
    }

    /// <summary>Directory holding key.dat / history.db / settings.json.</summary>
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipHive");

    /// <summary>
    /// Deletes the persisted key and the (now undecryptable) history database.
    /// Called from the startup recovery path when the key blob is corrupted.
    /// </summary>
    public static void ResetKeyAndData()
    {
        string dir = DataDirectory;
        File.Delete(Path.Combine(dir, "key.dat"));
        File.Delete(Path.Combine(dir, "history.db"));
        // WAL sidecar files, if present.
        File.Delete(Path.Combine(dir, "history.db-wal"));
        File.Delete(Path.Combine(dir, "history.db-shm"));
    }

    /// <summary>
    /// Loads or creates the persistent 256-bit key. On first run, generates a random
    /// key and stores it DPAPI-protected (CurrentUser scope) under
    /// %LOCALAPPDATA%/ClipHive/key.dat. On subsequent runs, loads and unprotects the
    /// same key so encrypted database content remains readable across restarts.
    /// The key is user-bound: DPAPI CurrentUser scope ties it to this Windows account.
    /// </summary>
    /// <exception cref="EncryptionKeyException">
    /// Thrown when the key cannot be loaded (corrupted key.dat, unavailable DPAPI,
    /// unreadable/unwritable data directory). ClipHive cannot safely store clipboard
    /// data without a user-bound key — no fallback is used because a predictable
    /// fallback key would defeat the encryption guarantee entirely. The app-level
    /// handler offers the user a key reset.
    /// </exception>
    private static byte[] DeriveKey()
    {
        string dir = DataDirectory;
        string keyFile = Path.Combine(dir, "key.dat");

        try
        {
            Directory.CreateDirectory(dir);

            if (File.Exists(keyFile))
            {
                byte[] protectedBlob = File.ReadAllBytes(keyFile);
                return ProtectedData.Unprotect(protectedBlob, null,
                    DataProtectionScope.CurrentUser);
            }

            // First run: generate random key, protect and persist it
            byte[] key = RandomNumberGenerator.GetBytes(KeySize);
            byte[] protected_ = ProtectedData.Protect(key, null,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(keyFile, protected_);
            return key;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException
                                      or UnauthorizedAccessException)
        {
            // Do NOT fall back to a predictable key — that would allow any attacker with
            // source-code access to decrypt the database. Fail with a recoverable error.
            throw new EncryptionKeyException(
                "ClipHive cannot load its encryption key for the current user. " +
                "The key file may be corrupted, or DPAPI (CurrentUser scope) is unavailable.",
                ex);
        }
    }
}
