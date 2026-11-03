using LinkPeek.Core;
using Xunit;

namespace LinkPeek.Core.Tests;

public sealed class PrDisplayTests
{
    private static readonly string Head = new('a', 40);

    private static Snapshot Snap(string status = "active", bool draft = false, int vote = 0) =>
        new(new PullRequest(7, "Title", status, draft, new Commit(Head), new Repository("id", "repo"), null, null, null), "me", vote);

    [Theory]
    [InlineData(0, PrDisplayState.Approve)]
    [InlineData(10, PrDisplayState.Approved)]
    [InlineData(5, PrDisplayState.ApprovedWithSuggestions)]
    [InlineData(-5, PrDisplayState.WaitingForAuthor)]
    [InlineData(-10, PrDisplayState.Rejected)]
    [InlineData(3, PrDisplayState.Unavailable)]
    public void VoteDecidesTheStateOfAnActivePullRequest(int vote, PrDisplayState expected) =>
        Assert.Equal(expected, PrDisplay.Resolve(Snap(vote: vote), null));

    [Fact]
    public void LifecycleBeatsVote()
    {
        Assert.Equal(PrDisplayState.Completed, PrDisplay.Resolve(Snap("completed"), null));
        Assert.Equal(PrDisplayState.Abandoned, PrDisplay.Resolve(Snap("abandoned", vote: 10), null));
        Assert.Equal(PrDisplayState.Unavailable, PrDisplay.Resolve(Snap("notSet"), null));
        Assert.Equal(PrDisplayState.Draft, PrDisplay.Resolve(Snap(draft: true), null));
    }

    // The reviewed head is the one this app approved earlier; the snapshot's head is newer.
    [Theory]
    [InlineData(10, PrDisplayState.NewCommits)]
    [InlineData(5, PrDisplayState.NewCommits)]
    [InlineData(0, PrDisplayState.Approve)]
    [InlineData(-5, PrDisplayState.WaitingForAuthor)]
    [InlineData(-10, PrDisplayState.Rejected)]
    public void StaleReviewOnlyWarnsWhileTheApprovalStillStands(int vote, PrDisplayState expected)
    {
        var older = new string('b', 40);
        Assert.Equal(expected, PrDisplay.Resolve(Snap(vote: vote), older));
        Assert.NotEqual(PrDisplayState.NewCommits, PrDisplay.Resolve(Snap(vote: vote), Head));
        Assert.NotEqual(PrDisplayState.NewCommits, PrDisplay.Resolve(Snap(vote: vote), null));
    }

    [Fact]
    public void OnlyApproveWaitingAndRejectedCanApprove()
    {
        var allowed = Enum.GetValues<PrDisplayState>().Where(s => s.CanApprove()).ToArray();
        Assert.Equal([PrDisplayState.Approve, PrDisplayState.WaitingForAuthor, PrDisplayState.Rejected], allowed);
    }

    [Fact]
    public void LabelsMatchTheMacCard()
    {
        Assert.Equal("Loading…", PrDisplayState.Loading.Label());
        Assert.Equal("Approve", PrDisplayState.Approve.Label());
        Assert.Equal("New Commits", PrDisplayState.NewCommits.Label());
        Assert.Equal("Configure PAT", PrDisplayState.Configure.Label());
    }
}
