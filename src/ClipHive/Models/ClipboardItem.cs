namespace ClipHive;

public sealed record ClipboardItem(
    long Id,
    string EncryptedContent,   // base64 ciphertext
    string Iv,                 // base64 12-byte nonce
    string Tag,                // base64 16-byte auth tag
    DateTime CreatedAt,
    string? SourceApp,
    bool IsPinned
);
