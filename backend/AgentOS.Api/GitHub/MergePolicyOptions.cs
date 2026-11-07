namespace AgentOS.Api.GitHub;

public sealed class MergePolicyRepositoryOptions
{
    public bool Enabled { get; set; }
    public int RequiredApprovals { get; set; } = 1;
    public bool RequireQualityGate { get; set; } = true;
    public bool RequireBranchProtection { get; set; }
}
