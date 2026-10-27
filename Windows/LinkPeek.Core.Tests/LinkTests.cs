using Xunit;

namespace LinkPeek.Core.Tests;

public sealed class LinkTests
{
    [Theory]
    [InlineData("https://example.org/a?q=hello%20world#fragment")]
    [InlineData("http://localhost:8080/test")]
    [InlineData("https://例え.jp/こんにちは")]
    public void WebTargetsPreserveOriginalText(string input) => Assert.Equal(input, WebLink.Parse(input)!.Text);

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.org")]
    [InlineData("Read https://example.org")]
    [InlineData("https://example.org\n")]
    [InlineData("https://example.org\\other")]
    public void RejectsNonTargets(string input) => Assert.Null(WebLink.Parse(input));

    [Fact]
    public void NormalizesOnlyKnownSchemeLessAccessibilityLinks()
    {
        Assert.Null(WebLink.Parse("dev.azure.com/org/p/_git/r/pullrequest/1"));
        Assert.NotNull(WebLink.Parse("dev.azure.com/org/p/_git/r/pullrequest/1", true)!.PullRequest);
        Assert.Null(WebLink.Parse("example.org/page", true));
    }

    [Fact]
    public void ParsesEncodedProjectWithoutDoubleEncoding()
    {
        var pr = WebLink.Parse("https://dev.azure.com/Org/My%20Project/_git/My%20Repo/pullrequest/123?x=y")!.PullRequest;
        Assert.Equal(new PullRequestLink("org", "My Project", "My Repo", "123"), pr);
    }

    [Theory]
    [InlineData("http://dev.azure.com/org/p/_git/r/pullrequest/1")]
    [InlineData("https://dev.azure.com.evil.test/org/p/_git/r/pullrequest/1")]
    [InlineData("https://dev.azure.com:444/org/p/_git/r/pullrequest/1")]
    [InlineData("https://dev.azure.com/org/p/_git/r/pullrequest/0")]
    [InlineData("https://dev.azure.com/org/p/_git/r/pullrequest/1/extra")]
    [InlineData("https://dev.azure.com/org/%2e%2e/_git/r/pullrequest/1")]
    [InlineData("https://dev.azure.com/org/p%2Fq/_git/r/pullrequest/1")]
    [InlineData("https://dev.azure.com/org/p/_git/r/pullrequest/18446744073709551616")]
    public void RejectsUntrustedPrPaths(string input) => Assert.Null(WebLink.Parse(input)!.PullRequest);

    [Fact]
    public void DwellStartsOnceAndResetsAcrossTargets()
    {
        var gate = new DwellGate();
        gate.Observe("a", TimeSpan.Zero);
        var generation = gate.Generation;
        Assert.False(gate.Start(TimeSpan.FromMilliseconds(499), TimeSpan.FromMilliseconds(500)));
        Assert.True(gate.Start(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500)));
        Assert.False(gate.Start(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(500)));
        gate.Observe("b", TimeSpan.FromSeconds(2));
        Assert.NotEqual(generation, gate.Generation);
        Assert.False(gate.Start(TimeSpan.FromMilliseconds(2100), TimeSpan.FromMilliseconds(500)));
        gate.Reset();
        Assert.False(gate.Start(TimeSpan.FromSeconds(5), TimeSpan.Zero));
    }
}
