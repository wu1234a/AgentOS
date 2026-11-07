namespace AgentOS.Domain.Quality;

public sealed class QualityGateRun
{
    public Guid Id { get; init; }
    public string Repository { get; init; } = string.Empty;
    public string HeadSha { get; init; } = string.Empty;
    public string WorkflowName { get; init; } = string.Empty;
    public long RunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Conclusion { get; set; }
    public int RunAttempt { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class QualityGateEvaluation
{
    public Guid Id { get; init; }
    public string Repository { get; init; } = string.Empty;
    public string HeadSha { get; init; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string ReasonCode { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
