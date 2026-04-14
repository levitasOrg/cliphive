namespace ClipHive;

public enum ClipboardContentType { Text, Image }

public enum ContentKind { Text, Url, HexColor, FilePath, Code }

public sealed record ClipboardItem(
    long Id,
    string EncryptedContent,   // decrypted plaintext for Text items; empty for Image items
    string Iv,                 // base64 12-byte nonce
    string Tag,                // base64 16-byte auth tag
    DateTime CreatedAt,
    string? SourceApp,
    bool IsPinned,
    ClipboardContentType ContentType = ClipboardContentType.Text,
    byte[]? ImageData = null,  // decoded image bytes (JPEG) for Image items; null for Text
    string? OcrText = null     // OCR-extracted text for Image items; null otherwise
);
