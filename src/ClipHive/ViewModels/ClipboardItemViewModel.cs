using System.Windows.Media.Imaging;
using System.IO;

namespace ClipHive.ViewModels;

/// <summary>
/// Wraps a <see cref="ClipboardItem"/> for display in the sidebar list.
/// Supports both text (preview + search) and image (thumbnail) content types.
/// </summary>
public sealed class ClipboardItemViewModel
{
    private readonly ClipboardItem _model;
    private readonly string _decryptedContent;

    public ClipboardItemViewModel(ClipboardItem model, string decryptedContent)
    {
        _model = model;
        _decryptedContent = decryptedContent;
        Id          = model.Id;
        IsPinned    = model.IsPinned;
        SourceApp   = model.SourceApp;
        ContentType = model.ContentType;
        ImageData   = model.ImageData;
        TimeAgo     = BuildTimeAgo(model.CreatedAt);

        if (model.ContentType == ClipboardContentType.Image)
        {
            Preview     = "[Image]";
            ImageSource = model.ImageData != null ? LoadBitmapFromBytes(model.ImageData) : null;
        }
        else
        {
            Preview = BuildPreview(decryptedContent);
        }
    }

    public long                  Id          { get; }
    public string                Preview     { get; }
    public string                TimeAgo     { get; }
    public bool                  IsPinned    { get; }
    public string?               SourceApp   { get; }
    public ClipboardContentType  ContentType { get; }
    public byte[]?               ImageData   { get; }
    public BitmapSource?         ImageSource { get; }

    public bool          IsImage          => ContentType == ClipboardContentType.Image;
    public string        DecryptedContent => _decryptedContent;
    public ClipboardItem Model            => _model;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildPreview(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var collapsed = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\s+", " ");
        return collapsed.Length <= 120 ? collapsed : collapsed[..120];
    }

    private static string BuildTimeAgo(DateTime createdAt)
    {
        var age = DateTime.UtcNow - createdAt.ToUniversalTime();
        return age.TotalSeconds switch
        {
            < 60     => "just now",
            < 3600   => $"{(int)age.TotalMinutes} min ago",
            < 7200   => "1 hour ago",
            < 86400  => $"{(int)age.TotalHours} hours ago",
            < 172800 => "yesterday",
            < 604800 => $"{(int)age.TotalDays} days ago",
            _        => createdAt.ToLocalTime().ToString("MMM d"),
        };
    }

    private static BitmapSource? LoadBitmapFromBytes(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption      = BitmapCacheOption.OnLoad;
            bmp.StreamSource     = ms;
            bmp.DecodePixelWidth = 200; // thumbnail size
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
