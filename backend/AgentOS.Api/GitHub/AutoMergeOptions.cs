namespace AgentOS.Api.GitHub;

/// <summary>
/// Enables a conservative auto-merge loop for AgentOS-created pull requests. A PR is eligible
/// only when it is open, mergeable, targets the configured base branch, uses the AgentOS branch
/// prefix, and has a successful independent review for the exact head SHA.
/// </summary>
public sealed class AutoMergeRepositoryOptions
{
    public bool Enabled { get; set; }

    public string BaseBranch { get; set; } = "main";

    public string BranchPrefix { get; set; } = "aos/";

    public string MergeMethod { get; set; } = "squash";

    public int PollSeconds { get; set; } = 30;

    public bool RequireReview { get; set; } = true;

    /// <summary>Ask GitHub to update stale branches before retrying the merge.</summary>
    public bool AutoUpdateBranch { get; set; } = true;
}
