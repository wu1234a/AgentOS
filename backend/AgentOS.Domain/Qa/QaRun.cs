namespace AgentOS.Domain.Qa;

public enum QaStatus
{
    Completed = 0,
    Faulted = 1
}

public enum QaConclusion
{
    Passed = 0,
    Failed = 1,
    Inconclusive = 2
}

public sealed class QaRun
{
    public Guid Id { get; init; }

    public Guid CodingRunId { get; init; }

    public int AttemptNo { get; init; }

    public QaStatus Status { get; set; }

    public QaConclusion? Conclusion { get; set; }

    public int CommandCount { get; set; }

    public int PassedCount { get; set; }

    public int FailedCount { get; set; }

    public string? ErrorSummary { get; set; }

    public DateTime CreatedAtUtc { get; init; }
}

public sealed class QaCommandResult
{
    public Guid Id { get; init; }

    public Guid QaRunId { get; init; }

    public int Ordinal { get; init; }

    public string CommandText { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int? ExitCode { get; init; }

    public int? DurationMs { get; init; }

    public string? OutputExcerpt { get; init; }
}
