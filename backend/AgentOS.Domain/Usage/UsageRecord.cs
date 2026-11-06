namespace AgentOS.Domain.Usage;

/// <summary>Token usage reported by an adapter, when the CLI provides credible numbers.</summary>
public sealed class UsageRecord
{
    public Guid Id { get; init; }

    public Guid TaskId { get; init; }

    public Guid AttemptId { get; init; }

    public Guid? WorkerId { get; init; }

    public string Provider { get; init; } = "codex";

    public string? Model { get; init; }

    public int InputTokens { get; init; }

    public int? CachedInputTokens { get; init; }

    public int OutputTokens { get; init; }

    public int? ReasoningTokens { get; init; }

    public int TotalTokens { get; init; }

    /// <summary><c>adapter</c> when measured, <c>unknown</c> when not provided.</summary>
    public string Source { get; init; } = "unknown";

    public DateTime CreatedAtUtc { get; init; }
}
