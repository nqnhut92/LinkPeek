namespace LinkPeek.Core;

public sealed record Approval(Guid Token, PullRequestLink Link, Snapshot Snapshot, DateTimeOffset Created);

public sealed class ApprovalService(IAzureDevOps client, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly Dictionary<Guid, Approval> confirmations = [];
    private readonly Dictionary<PullRequestLink, (string Reviewer, string Head)> reviewed = [];
    private readonly SemaphoreSlim mutation = new(1, 1);
    private readonly object sync = new();
    private bool invalidated;
    private const string Unverified = "Approval outcome unknown. Refresh before retrying.";

    public void Invalidate()
    {
        lock (sync) { invalidated = true; confirmations.Clear(); reviewed.Clear(); }
    }

    private void CheckActive()
    {
        lock (sync) if (invalidated) throw new UserError("Settings changed. Open the PR again.");
    }

    public string? ReviewedHead(PullRequestLink link, Snapshot snapshot)
    {
        lock (sync) return reviewed.TryGetValue(link, out var prior) && prior.Reviewer == snapshot.ReviewerId
            ? prior.Head : null;
    }

    private static void Validate(Snapshot latest, Snapshot expected)
    {
        if (latest.ReviewerId != expected.ReviewerId) throw new UserError("Authenticated reviewer changed.");
        if (latest.Head != expected.Head) throw new UserError("New commits. Review changes before approving.");
        if (!latest.Eligible) throw new UserError("This PR cannot be approved in its current state.");
    }

    public async Task<Approval> PrepareAsync(PullRequestLink link, Snapshot displayed, CancellationToken ct)
    {
        CheckActive();
        var latest = await client.SnapshotAsync(link, ct);
        Validate(latest, displayed);
        var result = new Approval(Guid.NewGuid(), link, latest, clock.GetUtcNow());
        lock (sync)
        {
            CheckActive();
            confirmations.Clear();
            confirmations.Add(result.Token, result);
        }
        return result;
    }

    public async Task<Snapshot> ApproveAsync(Approval approval, CancellationToken ct)
    {
        lock (sync)
        {
            CheckActive();
            if (!confirmations.Remove(approval.Token, out var stored) || stored != approval ||
                clock.GetUtcNow() - approval.Created > TimeSpan.FromMinutes(2))
                throw new UserError("Confirmation expired. Start again.");
        }
        if (!await mutation.WaitAsync(0, ct)) throw new UserError("Approval already in progress.");
        try
        {
            var latest = await client.SnapshotAsync(approval.Link, ct);
            CheckActive();
            Validate(latest, approval.Snapshot);
            ct.ThrowIfCancellationRequested();
            try
            {
                // Azure DevOps has no atomic head-SHA condition. Send once, never retry.
                var response = await client.VoteAsync(approval.Link, latest.ReviewerId, ct);
                if (response.Vote != 10 || !string.Equals(response.Id, latest.ReviewerId, StringComparison.OrdinalIgnoreCase))
                    throw new UserError(Unverified);
                var verified = await client.SnapshotAsync(approval.Link, ct);
                CheckActive();
                if (verified.Vote != 10 || verified.Head != latest.Head || verified.ReviewerId != latest.ReviewerId ||
                    verified.Pr.Status != "active" || verified.Pr.IsDraft) throw new UserError(Unverified);
                lock (sync) reviewed[approval.Link] = (verified.ReviewerId, verified.Head!);
                return verified;
            }
            catch (Exception error) when (error is UserError or HttpRequestException or OperationCanceledException)
            { throw new UserError(Unverified); }
        }
        finally { mutation.Release(); }
    }
}
