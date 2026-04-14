using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ClipHive;

/// <summary>
/// Extracts text from image bytes using the Windows built-in OCR engine.
/// Requires Windows 10 Build 17763 (1809) or later.
/// Returns null when the engine is unavailable or no text is found.
/// </summary>
internal static class OcrService
{
    /// <summary>
    /// Recognizes text in a JPEG image byte array using Windows.Media.Ocr.
    /// </summary>
    /// <param name="jpegBytes">Raw JPEG bytes to analyze.</param>
    /// <returns>Extracted text, or null if OCR is unavailable or yields nothing.</returns>
    public static async Task<string?> RecognizeTextAsync(byte[] jpegBytes)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return null;

        using var ms = new MemoryStream(jpegBytes);
        using var stream = ms.AsRandomAccessStream();

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(softBitmap);
        return string.IsNullOrWhiteSpace(result.Text) ? null : result.Text;
    }
}
