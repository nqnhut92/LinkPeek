using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LinkPeek.Core;

public sealed class UserError(string message) : Exception(message);

public sealed record Person(string Id, string? DisplayName = null, bool IsContainer = false);
public sealed record Commit(string CommitId);
public sealed record Repository(string Id, string Name);
public sealed record PullRequest(int PullRequestId, string Title, string Status, bool IsDraft,
    Commit? LastMergeSourceCommit, Repository Repository, Person? CreatedBy,
    string? SourceRefName, string? TargetRefName);
public sealed record Reviewer(string Id, int Vote, bool IsContainer = false);
public sealed record Snapshot(PullRequest Pr, string ReviewerId, int Vote)
{
    public string? Head => Pr.LastMergeSourceCommit?.CommitId;
    public bool Eligible => Pr.Status == "active" && !Pr.IsDraft &&
        Head is { Length: 40 } head && head.All(Uri.IsHexDigit) && Vote is 0 or -5 or -10;
    public string Caption => (Pr.Status == "active" && Pr.IsDraft ? "Draft" : Pr.Status) +
        " · Your vote: " + (Vote switch
        {
            10 => "Approved", 5 => "Approved with suggestions", 0 => "No vote",
            -5 => "Waiting for author", -10 => "Rejected", _ => "Unknown"
        });
}

public interface IAzureDevOps
{
    Task<Snapshot> SnapshotAsync(PullRequestLink link, CancellationToken cancellationToken);
    Task<Reviewer> VoteAsync(PullRequestLink link, string reviewerId, CancellationToken cancellationToken);
}

// A client captures one organization's credential for its entire lifetime. Settings
// changes dispose the old UI session rather than switching credentials mid-approval.
public sealed class AzureDevOpsClient : IAzureDevOps, IDisposable
{
    private readonly HttpClient http;
    private readonly string organization;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public AzureDevOpsClient(string organization, string token, HttpMessageHandler? handler = null)
    {
        if (!PullRequestLink.ValidOrganization(organization)) throw new UserError("Enter an organization name, not a URL.");
        if (string.IsNullOrWhiteSpace(token)) throw new UserError("Save a PAT in Settings first.");
        this.organization = organization.ToLowerInvariant();
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + token)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
    }

    private async Task<T> Request<T>(string[] segments, CancellationToken ct, object? body = null, bool versioned = true)
    {
        if (segments.Any(s => string.IsNullOrEmpty(s) || s is "." or "..")) throw new UserError("Invalid API path.");
        var url = "https://dev.azure.com/" + string.Join("/", segments.Select(Uri.EscapeDataString)) +
            (versioned ? "?api-version=7.1" : "");
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Put, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new UserError(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "PAT expired, revoked or invalid.",
                HttpStatusCode.Forbidden => "PAT or account lacks permission.",
                HttpStatusCode.NotFound => "PR unavailable or not found.",
                HttpStatusCode.TooManyRequests => "Rate limited. Retry later.",
                _ => $"Azure DevOps returned HTTP {(int)response.StatusCode}."
            });
            return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new UserError("Unexpected API response.");
        }
        catch (JsonException) { throw new UserError("Unexpected API response."); }
        catch (HttpRequestException) { throw new UserError("Network unavailable."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new UserError("Request timed out."); }
    }

    private string[] Path(PullRequestLink link, params string[] suffix)
    {
        if (!string.Equals(link.Organization, organization, StringComparison.OrdinalIgnoreCase))
            throw new UserError("Configure this organization in Settings first.");
        return [organization, link.Project, "_apis", "git", "repositories", link.Repository,
            "pullRequests", link.Id, .. suffix];
    }

    private sealed record Connection(Person AuthenticatedUser);
    private sealed record Reviewers(Reviewer[] Value);
    public async Task<string> IdentityAsync(CancellationToken ct)
    {
        var data = await Request<Connection>([organization, "_apis", "connectionData"], ct, versioned: false);
        if (data.AuthenticatedUser is not { IsContainer: false } user || !Guid.TryParse(user.Id, out var id))
            throw new UserError("Cannot verify authenticated reviewer.");
        return id.ToString();
    }

    public async Task<Snapshot> SnapshotAsync(PullRequestLink link, CancellationToken cancellationToken)
    {
        var path = Path(link);
        var identity = await IdentityAsync(cancellationToken);
        var pr = await Request<PullRequest>(path, cancellationToken);
        if (pr.Repository is null || !Guid.TryParse(pr.Repository.Id, out _) ||
            pr.PullRequestId.ToString(System.Globalization.CultureInfo.InvariantCulture) != link.Id ||
            string.IsNullOrEmpty(pr.Title) || string.IsNullOrEmpty(pr.Status) ||
            (pr.Status == "active" && !pr.IsDraft &&
                (pr.LastMergeSourceCommit?.CommitId is not { Length: 40 } head || !head.All(Uri.IsHexDigit))))
            throw new UserError("Unexpected API response.");
        var reviewers = await Request<Reviewers>(Path(link, "reviewers"), cancellationToken);
        if (reviewers.Value is null) throw new UserError("Unexpected API response.");
        var reviewer = reviewers.Value.FirstOrDefault(r => r is not null && !r.IsContainer &&
            string.Equals(r.Id, identity, StringComparison.OrdinalIgnoreCase));
        return new(pr, identity, reviewer?.Vote ?? 0);
    }

    public Task<Reviewer> VoteAsync(PullRequestLink link, string reviewerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(reviewerId, out _)) throw new UserError("Cannot verify authenticated reviewer.");
        return Request<Reviewer>(Path(link, "reviewers", reviewerId), cancellationToken,
            new { id = reviewerId, vote = 10 });
    }
    public void Dispose() => http.Dispose();
}
