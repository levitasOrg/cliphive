using System.Security.Cryptography;
using Xunit;

namespace ClipHive.Tests.Unit;

public sealed class EncryptionHelperTests
{
    // Use a fixed test key so DPAPI is not involved
    private static readonly byte[] TestKey = new byte[32]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
    };

    private static EncryptionHelper CreateHelper() => new(TestKey);

    // --- Round-trip ---

    [Fact]
    public void RoundTrip_AsciiString_ReturnsOriginal()
    {
        var helper = CreateHelper();
        const string plaintext = "Hello, ClipHive!";

        var (ciphertext, iv, tag) = helper.Encrypt(plaintext);
        string decrypted = helper.Decrypt(ciphertext, iv, tag);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void RoundTrip_EmptyString_ReturnsEmpty()
    {
        var helper = CreateHelper();

        var (ciphertext, iv, tag) = helper.Encrypt(string.Empty);
        string decrypted = helper.Decrypt(ciphertext, iv, tag);

        Assert.Equal(string.Empty, decrypted);
    }

    [Fact]
    public void RoundTrip_UnicodeString_ReturnsOriginal()
    {
        var helper = CreateHelper();
        const string plaintext = "日本語テスト 🚀 مرحبا";

        var (ciphertext, iv, tag) = helper.Encrypt(plaintext);
        string decrypted = helper.Decrypt(ciphertext, iv, tag);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void RoundTrip_LongString_ReturnsOriginal()
    {
        var helper = CreateHelper();
        string plaintext = new string('A', 10_000);

        var (ciphertext, iv, tag) = helper.Encrypt(plaintext);
        string decrypted = helper.Decrypt(ciphertext, iv, tag);

        Assert.Equal(plaintext, decrypted);
    }

    // --- IV uniqueness ---

    [Fact]
    public void Encrypt_SamePlaintext_ProducesDifferentCiphertexts()
    {
        var helper = CreateHelper();
        const string plaintext = "same input";

        var (ct1, iv1, _) = helper.Encrypt(plaintext);
        var (ct2, iv2, _) = helper.Encrypt(plaintext);

        // IVs must differ
        Assert.NotEqual(iv1, iv2);
        // Ciphertexts will also differ because the IV is different
        Assert.NotEqual(ct1, ct2);
    }

    [Fact]
    public void Encrypt_ProducesBase64Iv_Of12Bytes()
    {
        var helper = CreateHelper();
        var (_, iv, _) = helper.Encrypt("test");

        byte[] ivBytes = Convert.FromBase64String(iv);
        Assert.Equal(12, ivBytes.Length);
    }

    [Fact]
    public void Encrypt_ProducesBase64Tag_Of16Bytes()
    {
        var helper = CreateHelper();
        var (_, _, tag) = helper.Encrypt("test");

        byte[] tagBytes = Convert.FromBase64String(tag);
        Assert.Equal(16, tagBytes.Length);
    }

    // --- Tampering ---

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        var helper = CreateHelper();
        var (ciphertext, iv, tag) = helper.Encrypt("sensitive data");

        // Flip the first byte of the ciphertext
        byte[] ctBytes = Convert.FromBase64String(ciphertext);
        ctBytes[0] ^= 0xFF;
        string tampered = Convert.ToBase64String(ctBytes);

        Assert.ThrowsAny<CryptographicException>(() => helper.Decrypt(tampered, iv, tag));
    }

    [Fact]
    public void Decrypt_TamperedTag_ThrowsCryptographicException()
    {
        var helper = CreateHelper();
        var (ciphertext, iv, tag) = helper.Encrypt("sensitive data");

        byte[] tagBytes = Convert.FromBase64String(tag);
        tagBytes[0] ^= 0xFF;
        string tampered = Convert.ToBase64String(tagBytes);

        Assert.ThrowsAny<CryptographicException>(() => helper.Decrypt(ciphertext, iv, tampered));
    }

    [Fact]
    public void Decrypt_TamperedIv_ThrowsCryptographicException()
    {
        var helper = CreateHelper();
        var (ciphertext, iv, tag) = helper.Encrypt("sensitive data");

        byte[] ivBytes = Convert.FromBase64String(iv);
        ivBytes[0] ^= 0xFF;
        string tampered = Convert.ToBase64String(ivBytes);

        Assert.ThrowsAny<CryptographicException>(() => helper.Decrypt(ciphertext, tampered, tag));
    }

    // --- Null handling ---

    [Fact]
    public void Encrypt_NullPlaintext_ThrowsArgumentNullException()
    {
        var helper = CreateHelper();
        Assert.Throws<ArgumentNullException>(() => helper.Encrypt(null!));
    }

    [Fact]
    public void Decrypt_NullCiphertext_ThrowsArgumentNullException()
    {
        var helper = CreateHelper();
        Assert.Throws<ArgumentNullException>(() => helper.Decrypt(null!, "aaa", "bbb"));
    }

    // --- Constructor ---

    [Fact]
    public void Constructor_WrongKeyLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new EncryptionHelper(new byte[16]));
    }

    [Fact]
    public void Constructor_NullKey_UsesDerivedKey_DoesNotThrow()
    {
        // Should not throw — falls back to DPAPI or SHA-256 fallback
        var helper = new EncryptionHelper(null);
        var (ct, iv, tag) = helper.Encrypt("ping");
        string result = helper.Decrypt(ct, iv, tag);
        Assert.Equal("ping", result);
    }
}
