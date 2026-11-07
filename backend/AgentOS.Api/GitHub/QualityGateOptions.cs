namespace AgentOS.Api.GitHub;

public sealed class QualityGateRepositoryOptions
{
    public bool Enabled { get; set; }
    public string[] RequiredWorkflows { get; set; } = [];
    public string[] AllowedConclusions { get; set; } = ["success"];
}
