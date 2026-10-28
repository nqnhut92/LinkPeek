using Xunit;

namespace LinkPeek.Core.Tests;

public sealed class LinkFilterTests
{
    private static Uri U(string text) => new(text);

    [Fact]
    public void GrabShortLinkIsAllowedByDefault()
    {
        var filter = new LinkFilter();
        Assert.True(filter.AllowsQr(U("https://app.grab.com/s/QIe9bSIw")));
        Assert.False(filter.AllowsQr(U("https://app.grab.com/")));
        Assert.False(filter.AllowsQr(U("https://github.com/nqnhut92/LinkPeek")));
    }

    [Fact]
    public void MasterAndGrabSwitchesDisableQr()
    {
        var filter = new LinkFilter(Patterns: ["github.com/*"], AnyOtherLink: true) with { QrEnabled = false };
        Assert.False(filter.AllowsQr(U("https://app.grab.com/s/abc")));
        filter = filter with { QrEnabled = true, GrabEnabled = false, AnyOtherLink = false };
        Assert.False(filter.AllowsQr(U("https://app.grab.com/s/abc")));
        Assert.True(filter.AllowsQr(U("https://github.com/a/b")));
    }

    [Fact]
    public void AnyOtherLinkAllowsEverything() =>
        Assert.True(new LinkFilter(AnyOtherLink: true).AllowsQr(U("https://example.com/x?y=1")));

    [Fact]
    public void PatternsAreCaseInsensitiveAndIgnoreSchemeQueryAndPort()
    {
        Assert.True(LinkPattern.Matches("GitHub.com/Org/*", U("http://github.com/org/repo?tab=1")));
        Assert.True(LinkPattern.Matches("*.notion.so/*", U("https://team.notion.so/page")));
        Assert.False(LinkPattern.Matches("*.notion.so/*", U("https://notion.so/page")));
        Assert.True(LinkPattern.Matches("example.com/a", U("https://example.com:8080/a/")));
    }

    [Fact]
    public void HostOnlyPatternCoversEveryPath()
    {
        Assert.True(LinkPattern.Matches("example.com", U("https://example.com")));
        Assert.True(LinkPattern.Matches("example.com", U("https://example.com/a/b")));
        Assert.False(LinkPattern.Matches("example.com", U("https://notexample.com/a")));
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("*")]
    [InlineData("**/")]
    [InlineData("a b/c")]
    public void NormalizeRejectsEmptyWhitespaceAndBareWildcards(string input) => Assert.Null(LinkPattern.Normalize(input));

    [Fact]
    public void NormalizeStripsSchemeAndTrailingSlash()
    {
        Assert.Equal("github.com/org", LinkPattern.Normalize("https://GitHub.com/Org/"));
        Assert.Equal("github.com/*", LinkPattern.Normalize("github.com"));
    }

    [Theory]
    [InlineData(0, 0, 800)]
    [InlineData(500, 0, 800)]
    [InlineData(500, 2, 500)]
    [InlineData(350, 0, 500)]
    [InlineData(1000, 0, 1000)]
    [InlineData(9000, 2, 1500)]
    public void DelayMigrationMovesOldDefaultOnceAndClamps(int stored, int version, int expected) =>
        Assert.Equal(expected, HoverDelay.Resolve(stored, version));
}
