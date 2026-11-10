namespace AgentOS.Api.GitHub;

public sealed class GitHubOptions
{
    public bool Enabled { get; set; }
    public string? WebhookSecret { get; set; }
    public string[] AllowedRepositories { get; set; } = [];
    public string[] AllowedEvents { get; set; } = ["pull_request", "push", "workflow_run"];
    public Dictionary<string, string> RepositoryProjects { get; set; } = [];
    public bool ReviewEnabled { get; set; }
    public string? ReviewToken { get; set; }
    public int MaxReviewAttempts { get; set; } = 5;
    public Dictionary<string, QualityGateRepositoryOptions> QualityGates { get; set; } = [];
    public Dictionary<string, MergePolicyRepositoryOptions> MergePolicies { get; set; } = [];
    public Dictionary<string, AutoMergeRepositoryOptions> AutoMerges { get; set; } = [];
}
