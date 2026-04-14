using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.IO;

namespace ClipHive.ViewModels;

/// <summary>
/// Wraps a <see cref="ClipboardItem"/> for display in the sidebar list.
/// Supports both text (preview + search) and image (thumbnail) content types.
/// Detects content kind (URL, hex color, file path, code) for contextual actions.
/// </summary>
public sealed class ClipboardItemViewModel
{
    private readonly ClipboardItem _model;
    private readonly string _decryptedContent;

    // ── Compiled patterns ──────────────────────────────────────────────────────
    private static readonly Regex UrlPattern      = new(@"^https?://\S+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HexPattern      = new(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);
    private static readonly Regex FilePathPattern = new(@"^([A-Za-z]:\\|\\\\)", RegexOptions.Compiled);

    private static readonly string[] CodeKeywords =
    [
        "public class", "namespace ", "using System",           // C#
        "def ", "elif ", "isinstance(", "print(",               // Python
        "function ", "const ", "let ", "=>", "console.log",    // JS/TS
        "SELECT ", "INSERT INTO", "CREATE TABLE",               // SQL
        "```"                                                   // Markdown
    ];

    public ClipboardItemViewModel(ClipboardItem model, string decryptedContent)
    {
        _model = model;
        _decryptedContent = decryptedContent;
        Id          = model.Id;
        IsPinned    = model.IsPinned;
        SourceApp   = model.SourceApp;
        ContentType = model.ContentType;
        ImageData   = model.ImageData;
        OcrText     = model.OcrText;
        TimeAgo     = BuildTimeAgo(model.CreatedAt);

        if (model.ContentType == ClipboardContentType.Image)
        {
            Kind        = ContentKind.Text; // images don't have a text kind
            Preview     = !string.IsNullOrWhiteSpace(model.OcrText)
                            ? "📷 " + BuildPreview(model.OcrText, maxLength: 80)
                            : "[Image]";
            ImageSource = model.ImageData != null ? LoadBitmapFromBytes(model.ImageData) : null;
            OpenActionCommand = new RelayCommand(() => { }); // no-op for images
        }
        else
        {
            var trimmed = decryptedContent.Trim();
            Kind    = DetectKind(trimmed);
            Preview = BuildPreview(decryptedContent);

            if (Kind == ContentKind.HexColor)
                HexColorBrush = ParseHexBrush(trimmed);

            if (Kind == ContentKind.Code)
                DetectedLanguage = DetectLanguage(trimmed);

            OpenActionCommand = new RelayCommand(() => ExecuteOpenAction(trimmed, Kind));
        }
    }

    // ── Properties ─────────────────────────────────────────────────────────────

    public long                  Id               { get; }
    public string                Preview          { get; }
    public string                TimeAgo          { get; }
    public bool                  IsPinned         { get; }
    public string?               SourceApp        { get; }
    public ClipboardContentType  ContentType      { get; }
    public byte[]?               ImageData        { get; }
    public BitmapSource?         ImageSource      { get; }
    public string?               OcrText          { get; }
    public ContentKind           Kind             { get; }
    public System.Windows.Media.SolidColorBrush? HexColorBrush { get; }
    public string                DetectedLanguage { get; } = string.Empty;
    public ICommand              OpenActionCommand { get; }

    public bool          IsImage          => ContentType == ClipboardContentType.Image;
    public string        DecryptedContent => _decryptedContent;
    public ClipboardItem Model            => _model;

    // ── Kind detection ─────────────────────────────────────────────────────────

    private static ContentKind DetectKind(string trimmed)
    {
        if (UrlPattern.IsMatch(trimmed))      return ContentKind.Url;
        if (HexPattern.IsMatch(trimmed))      return ContentKind.HexColor;
        if (FilePathPattern.IsMatch(trimmed)) return ContentKind.FilePath;
        if (IsLikelyCode(trimmed))            return ContentKind.Code;
        return ContentKind.Text;
    }

    private static bool IsLikelyCode(string content) =>
        content.Contains('\n') &&
        CodeKeywords.Any(kw => content.Contains(kw, StringComparison.Ordinal));

    private static string DetectLanguage(string content)
    {
        if (content.Contains("public class") || content.Contains("namespace ") || content.Contains("using System"))
            return "C#";
        if (content.Contains("def ") || content.Contains("elif ") || content.Contains("import "))
            return "Python";
        if (content.Contains("function ") || content.Contains("console.log") || content.Contains("=>"))
            return "JavaScript";
        if (content.Contains("SELECT ") || content.Contains("INSERT INTO"))
            return "TSQL";
        return string.Empty;
    }

    // ── Action execution ───────────────────────────────────────────────────────

    private static void ExecuteOpenAction(string content, ContentKind kind)
    {
        try
        {
            if (kind == ContentKind.Url)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(content) { UseShellExecute = true });
            else if (kind == ContentKind.FilePath)
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{content}\"");
        }
        catch { /* non-fatal: open action failure */ }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string BuildPreview(string content, int maxLength = 120)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var collapsed = Regex.Replace(content.Trim(), @"\s+", " ");
        return collapsed.Length <= maxLength ? collapsed : collapsed[..maxLength];
    }

    private static System.Windows.Media.SolidColorBrush? ParseHexBrush(string hex)
    {
        try
        {
            return new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }
        catch { return null; }
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
