using Xunit;

namespace LinkPeek.Core.Tests;

public sealed class ApprovalTests
{
    private static readonly PullRequestLink Link = new("org", "p", "r", "1");
    private static Snapshot State(string head = "a", int vote = 0, string status = "active", bool draft = false,
        string reviewer = "reviewer") => new(new PullRequest(1, "Change", status, draft,
            new Commit(new string(head[0], 40)), new Repository("repo", "repo"), null, "source", "target"), reviewer, vote);

    private sealed class FakeClient(params Snapshot[] snapshots) : IAzureDevOps
    {
        public Queue<Snapshot> States { get; } = new(snapshots);
        public int Votes { get; private set; }
        public bool FailVote { get; set; }
        public Task<Snapshot> SnapshotAsync(PullRequestLink link, CancellationToken ct) => Task.FromResult(States.Dequeue());
        public Task<Reviewer> VoteAsync(PullRequestLink link, string reviewerId, CancellationToken ct)
        {
            Votes++;
            if (FailVote) throw new UserError("Request timed out.");
            return Task.FromResult(new Reviewer(reviewerId, 10));
        }
    }

    [Fact]
    public async Task ApprovesOnceAndRecordsOnlyVerifiedHead()
    {
        var client = new FakeClient(State(), State(), State(vote: 10));
        var service = new ApprovalService(client);
        var prepared = await service.PrepareAsync(Link, State(), default);
        var result = await service.ApproveAsync(prepared, default);
        Assert.Equal(10, result.Vote);
        Assert.Equal(result.Head, service.ReviewedHead(Link, result));
        await Assert.ThrowsAsync<UserError>(() => service.ApproveAsync(prepared, default));
        Assert.Equal(1, client.Votes);
    }

    [Theory]
    [InlineData("b", 0, "active", false, "reviewer")]
    [InlineData("a", 10, "active", false, "reviewer")]
    [InlineData("a", 5, "active", false, "reviewer")]
    [InlineData("a", 0, "completed", false, "reviewer")]
    [InlineData("a", 0, "active", true, "reviewer")]
    [InlineData("a", 42, "active", false, "reviewer")]
    [InlineData("a", 0, "active", false, "different-reviewer")]
    public async Task RevalidatesBeforeSendingAnyVote(string head, int vote, string status, bool draft, string reviewer)
    {
        var client = new FakeClient(State(), State(head, vote, status, draft, reviewer));
        var service = new ApprovalService(client);
        var prepared = await service.PrepareAsync(Link, State(), default);
        await Assert.ThrowsAsync<UserError>(() => service.ApproveAsync(prepared, default));
        Assert.Equal(0, client.Votes);
    }

    [Fact]
    public async Task AmbiguousMutationIsNeverRetried()
    {
        var client = new FakeClient(State(), State()) { FailVote = true };
        var service = new ApprovalService(client);
        var prepared = await service.PrepareAsync(Link, State(), default);
        var error = await Assert.ThrowsAsync<UserError>(() => service.ApproveAsync(prepared, default));
        Assert.Contains("outcome unknown", error.Message);
        Assert.Null(service.ReviewedHead(Link, State()));
        await Assert.ThrowsAsync<UserError>(() => service.ApproveAsync(prepared, default));
        Assert.Equal(1, client.Votes);
    }

    [Fact]
    public async Task DoesNotClaimSuccessIfPostVoteHeadChanged()
    {
        var client = new FakeClient(State(), State(), State("b", 10));
        var service = new ApprovalService(client);
        var prepared = await service.PrepareAsync(Link, State(), default);
        await Assert.ThrowsAsync<UserError>(() => service.ApproveAsync(prepared, default));
        Assert.Null(service.ReviewedHead(Link, State()));
        Assert.Equal(1, client.Votes);
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task ExpiredConfirmationCannotSendVote()
    {
        var clock = new FakeClock();
        var client = new FakeClient(State());
        var service = new ApprovalService(client, clock);
        var prepared = await service.PrepareAsync(Link, State(), default);
        clock.Now += TimeSpan.FromSeconds(121);
        await Assert.ThrowsAsync<UserError>(() => service.ApproveAsync(prepared, default));
        Assert.Equal(0, client.Votes);
    }

    [Fact]
    public async Task SettingsChangesInvalidateConfirmation()
    {
        var client = new FakeClient(State());
        var service = new ApprovalService(client);
        var prepared = await service.PrepareAsync(Link, State(), default);
        service.Invalidate();
        await Assert.ThrowsAsync<UserError>(() => service.ApproveAsync(prepared, default));
        Assert.Equal(0, client.Votes);
    }
}
