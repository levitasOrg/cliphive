using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClipHive;

/// <summary>
/// AES-256-GCM encryption helper with optional DPAPI key derivation.
/// Implements IEncryptionHelper for dependency injection.
/// </summary>
public sealed class EncryptionHelper : IEncryptionHelper
{
    private readonly byte[] _key;
    private const int IvSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256-bit

    /// <summary>
    /// Constructs an EncryptionHelper.
    /// If <paramref name="key"/> is null, derives the key via DPAPI (machine-bound).
    /// If provided, uses the supplied key directly (useful for testing).
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

    /// <summary>
    /// Derives a persistent 256-bit key. On first run, generates a random key and
    /// stores it DPAPI-protected (CurrentUser scope) under %LOCALAPPDATA%/ClipHive/key.dat.
    /// On subsequent runs, loads and unprotects the same key so encrypted database
    /// content remains readable across restarts.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when DPAPI is unavailable. ClipHive cannot safely store clipboard data
    /// without a user-bound key — no fallback is used because a predictable fallback
    /// key would defeat the encryption guarantee entirely.
    /// </exception>
    private static byte[] DeriveKey()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipHive");
        Directory.CreateDirectory(dir);
        string keyFile = Path.Combine(dir, "key.dat");

        try
        {
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
        catch (CryptographicException ex)
        {
            // Do NOT fall back to a predictable key — that would allow any attacker with
            // source-code access to decrypt the database. Fail loudly instead.
            throw new InvalidOperationException(
                "ClipHive cannot derive an encryption key for the current user. " +
                "This may be caused by a corrupted Windows user profile or " +
                "missing DPAPI support (DataProtectionScope.CurrentUser). " +
                "Please report this issue at https://github.com/gokulMv/ClipHive/issues",
                ex);
        }
    }
}
