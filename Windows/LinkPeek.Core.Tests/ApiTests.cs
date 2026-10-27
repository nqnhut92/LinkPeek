using System.Net;
using System.Text;
using Xunit;

namespace LinkPeek.Core.Tests;

public sealed class ApiTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request));
    }

    [Fact]
    public async Task EncodesApiPathsAndScopesCredentialsToConfiguredOrganization()
    {
        var paths = new List<string>();
        using var client = new AzureDevOpsClient("org", "test-only-token", new Handler(request =>
        {
            paths.Add(request.RequestUri!.AbsoluteUri);
            Assert.Equal("dev.azure.com", request.RequestUri.Host);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            var body = paths.Count switch
            {
                1 => "{\"authenticatedUser\":{\"id\":\"11111111-1111-1111-1111-111111111111\"}}",
                2 => "{\"pullRequestId\":1,\"title\":\"Test\",\"status\":\"active\",\"isDraft\":false,\"lastMergeSourceCommit\":{\"commitId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"},\"repository\":{\"id\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"repo\"}}",
                _ => "{\"value\":[]}"
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }));
        var link = WebLink.Parse("https://dev.azure.com/org/My%20Project/_git/My%20Repo/pullrequest/1")!.PullRequest!;
        var result = await client.SnapshotAsync(link, default);
        Assert.True(result.Eligible);
        Assert.Equal("https://dev.azure.com/org/_apis/connectionData", paths[0]);
        Assert.Contains("My%20Project/_apis/git/repositories/My%20Repo/pullRequests/1?api-version=7.1", paths[1]);
        await Assert.ThrowsAsync<UserError>(() => client.SnapshotAsync(link with { Organization = "another-org" }, default));
        Assert.Equal(3, paths.Count);
    }

    [Theory]
    [InlineData(401, "PAT expired")]
    [InlineData(403, "lacks permission")]
    [InlineData(429, "Rate limited")]
    [InlineData(302, "HTTP 302")]
    public async Task ReportsSafeErrorsWithoutReturningServerBody(int status, string expected)
    {
        using var client = new AzureDevOpsClient("org", "test-only-token", new Handler(_ =>
            new((HttpStatusCode)status) { Content = new StringContent("sensitive-server-body") }));
        var error = await Assert.ThrowsAsync<UserError>(() => client.IdentityAsync(default));
        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("sensitive-server-body", error.Message);
    }
}
