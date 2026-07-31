using ClipHive.ViewModels;
using Xunit;

namespace ClipHive.Tests.Unit;

public sealed class ClipboardItemViewModelTests
{
    private static ClipboardItemViewModel Create(string content) =>
        new(new ClipboardItem(1, content, "iv==", "tag==", DateTime.UtcNow, null, false), content);

    // ── Kind detection ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com/page?x=1", ContentKind.Url)]
    [InlineData("http://localhost:8080", ContentKind.Url)]
    [InlineData("#FF5733", ContentKind.HexColor)]
    [InlineData("#abc", ContentKind.HexColor)]
    [InlineData(@"C:\Users\me\file.txt", ContentKind.FilePath)]
    [InlineData(@"\\server\share\doc.pdf", ContentKind.FilePath)]
    [InlineData("just a sentence", ContentKind.Text)]
    public void Kind_IsDetectedFromContent(string content, ContentKind expected)
    {
        Assert.Equal(expected, Create(content).Kind);
    }

    [Theory]
    [InlineData("using System;\npublic class Foo { }", "C#")]
    [InlineData("def main():\n    print('hi')", "Python")]
    [InlineData("const x = 1;\nconsole.log(x)", "JavaScript")]
    [InlineData("SELECT *\nFROM users", "TSQL")]
    public void Code_DetectsLanguage(string content, string language)
    {
        var vm = Create(content);
        Assert.Equal(ContentKind.Code, vm.Kind);
        Assert.Equal(language, vm.DetectedLanguage);
    }

    [Fact]
    public void HexColor_GetsSwatchBrush()
    {
        Assert.NotNull(Create("#FF5733").HexColorBrush);
        Assert.Null(Create("plain text").HexColorBrush);
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    [Fact]
    public void Preview_CollapsesWhitespace_AndTruncatesTo120()
    {
        var vm = Create("hello   \n\t world " + new string('x', 300));
        Assert.StartsWith("hello world", vm.Preview);
        Assert.Equal(120, vm.Preview.Length);
    }

    [Fact]
    public void ImageItem_WithOcr_PreviewShowsCameraPrefix()
    {
        var item = new ClipboardItem(2, string.Empty, "iv==", "tag==", DateTime.UtcNow,
            null, false, ClipboardContentType.Image, new byte[] { 1 }, "invoice 42");
        var vm = new ClipboardItemViewModel(item, string.Empty);

        Assert.True(vm.IsImage);
        Assert.Contains("invoice 42", vm.Preview);
    }

    [Fact]
    public void ImageItem_WithoutOcr_PreviewIsImagePlaceholder()
    {
        var item = new ClipboardItem(2, string.Empty, "iv==", "tag==", DateTime.UtcNow,
            null, false, ClipboardContentType.Image, new byte[] { 1 }, null);
        Assert.Equal("[Image]", new ClipboardItemViewModel(item, string.Empty).Preview);
    }

    // ── TimeAgo ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(-5, "5 min ago")]
    [InlineData(-90, "1 hour ago")]
    [InlineData(-60 * 5, "5 hours ago")]
    [InlineData(-60 * 30, "yesterday")]
    public void TimeAgo_FormatsAge(int minutesOffset, string expected)
    {
        var item = new ClipboardItem(1, "x", "iv==", "tag==",
            DateTime.UtcNow.AddMinutes(minutesOffset), null, false);
        Assert.Equal(expected, new ClipboardItemViewModel(item, "x").TimeAgo);
    }
}
