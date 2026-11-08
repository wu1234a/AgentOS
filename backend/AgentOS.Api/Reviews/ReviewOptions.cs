namespace AgentOS.Api.Reviews;

public sealed class ReviewOptions
{
    /// <summary>codex-cli uses the Codex CLI reviewer; any other value uses the OpenAI-compatible/Anthropic adapter.</summary>
    public string Provider { get; set; } = "codex-cli";
    public string? Model { get; set; }
    public string? BaseUrl { get; set; }
    public string? SecretReference { get; set; }
    public int TimeoutSeconds { get; set; } = 600;
}
