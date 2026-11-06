namespace AgentOS.Domain.ModelProfiles;

/// <summary>Model/execution profile referenced by agents and rules. Never stores a secret value.</summary>
public sealed class ModelProfile
{
    public Guid Id { get; init; }

    public string Code { get; set; } = string.Empty;

    /// <summary>For example <c>codex-cli</c>, <c>openai-api</c>.</summary>
    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary><c>cli</c> or <c>api</c>.</summary>
    public string ExecutionMode { get; set; } = "cli";

    /// <summary>Non-sensitive provider parameters as JSON.</summary>
    public string ConfigurationJson { get; set; } = "{}";

    /// <summary>Name of an environment variable or secret store entry; never the value.</summary>
    public string? SecretReference { get; set; }

    public int TimeoutSeconds { get; set; } = 1800;

    public decimal? MaxCostPerTask { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
