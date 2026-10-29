namespace LinkPeek.Core;

/// <summary>What the pull request card shows and offers. Same states and labels as the macOS card.</summary>
public enum PrDisplayState
{
    Loading, Approve, Approved, ApprovedWithSuggestions, WaitingForAuthor, Rejected,
    Abandoned, Unavailable, NewCommits, Completed, Draft, Error, Configure
}

public static class PrDisplay
{
    public static bool CanApprove(this PrDisplayState state) =>
        state is PrDisplayState.Approve or PrDisplayState.WaitingForAuthor or PrDisplayState.Rejected;

    public static string Label(this PrDisplayState state) => state switch
    {
        PrDisplayState.Loading => "Loading…",
        PrDisplayState.Approve => "Approve",
        PrDisplayState.Approved => "Approved",
        PrDisplayState.ApprovedWithSuggestions => "Approved with suggestions",
        PrDisplayState.WaitingForAuthor => "Waiting for author",
        PrDisplayState.Rejected => "Rejected",
        PrDisplayState.Abandoned => "Abandoned",
        PrDisplayState.NewCommits => "New Commits",
        PrDisplayState.Completed => "Completed",
        PrDisplayState.Draft => "Draft",
        PrDisplayState.Error => "Error",
        PrDisplayState.Configure => "Configure PAT",
        _ => "Unavailable"
    };

    /// <param name="reviewedHead">The head this reviewer already approved, if any.</param>
    public static PrDisplayState Resolve(Snapshot snapshot, string? reviewedHead)
    {
        if (snapshot.Pr.Status == "completed") return PrDisplayState.Completed;
        if (snapshot.Pr.Status == "abandoned") return PrDisplayState.Abandoned;
        if (snapshot.Pr.Status != "active") return PrDisplayState.Unavailable;
        if (snapshot.Pr.IsDraft) return PrDisplayState.Draft;
        if (reviewedHead is not null && reviewedHead != snapshot.Head) return PrDisplayState.NewCommits;
        return snapshot.Vote switch
        {
            10 => PrDisplayState.Approved,
            5 => PrDisplayState.ApprovedWithSuggestions,
            -5 => PrDisplayState.WaitingForAuthor,
            -10 => PrDisplayState.Rejected,
            0 => PrDisplayState.Approve,
            _ => PrDisplayState.Unavailable
        };
    }
}
