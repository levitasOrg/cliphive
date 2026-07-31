using Xunit;

namespace ClipHive.Tests.Unit;

public sealed class CaptureFilterTests
{
    private static CaptureFilter Create(string[]? apps = null, string[]? patterns = null)
    {
        var filter = new CaptureFilter();
        filter.Update(apps, patterns);
        return filter;
    }

    // ── App exclusions ────────────────────────────────────────────────────────

    [Fact]
    public void ShouldIgnoreApp_ListedApp_IsIgnored()
    {
        var filter = Create(apps: new[] { "KeePass" });
        Assert.True(filter.ShouldIgnoreApp("KeePass"));
    }

    [Theory]
    [InlineData("keepass")]
    [InlineData("KEEPASS")]
    [InlineData("KeePass.exe")]
    public void ShouldIgnoreApp_MatchesCaseInsensitive_AndIgnoresExeSuffix(string sourceApp)
    {
        var filter = Create(apps: new[] { "KeePass" });
        Assert.True(filter.ShouldIgnoreApp(sourceApp));
    }

    [Fact]
    public void ShouldIgnoreApp_ListEntryWithExeSuffix_StillMatches()
    {
        var filter = Create(apps: new[] { "Bitwarden.exe" });
        Assert.True(filter.ShouldIgnoreApp("Bitwarden"));
    }

    [Fact]
    public void ShouldIgnoreApp_UnlistedApp_IsNotIgnored()
    {
        var filter = Create(apps: new[] { "KeePass" });
        Assert.False(filter.ShouldIgnoreApp("Notepad"));
    }

    [Fact]
    public void ShouldIgnoreApp_NullSource_IsNotIgnored()
    {
        // Unknown source (delayed rendering, UWP broker) must not block capture.
        var filter = Create(apps: new[] { "KeePass" });
        Assert.False(filter.ShouldIgnoreApp(null));
    }

    [Fact]
    public void ShouldIgnoreApp_EmptyList_NothingIgnored()
    {
        var filter = Create();
        Assert.False(filter.ShouldIgnoreApp("KeePass"));
    }

    // ── Pattern exclusions ────────────────────────────────────────────────────

    [Fact]
    public void ShouldIgnoreText_MatchingPattern_IsIgnored()
    {
        var filter = Create(patterns: new[] { @"^ghp_[A-Za-z0-9]+$" });
        Assert.True(filter.ShouldIgnoreText("ghp_abc123DEF"));
        Assert.False(filter.ShouldIgnoreText("just some prose"));
    }

    [Fact]
    public void ShouldIgnoreText_PatternIsCaseInsensitive()
    {
        var filter = Create(patterns: new[] { "confidential" });
        Assert.True(filter.ShouldIgnoreText("This is CONFIDENTIAL material"));
    }

    [Fact]
    public void ShouldIgnoreText_InvalidRegex_IsSkippedWithoutBreakingOthers()
    {
        var filter = Create(patterns: new[] { "[unclosed", "^secret$" });
        Assert.True(filter.ShouldIgnoreText("secret"));
        Assert.False(filter.ShouldIgnoreText("[unclosed"));
    }

    [Fact]
    public void Update_ReplacesPreviousLists()
    {
        var filter = Create(apps: new[] { "KeePass" }, patterns: new[] { "^a$" });
        filter.Update(null, null);
        Assert.False(filter.ShouldIgnoreApp("KeePass"));
        Assert.False(filter.ShouldIgnoreText("a"));
    }
}
