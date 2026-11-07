namespace AgentOS.Domain.Reviews;

public enum ReviewStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

public enum ReviewConclusion
{
    NoIssues = 0,
    IssuesFound = 1
}

public enum ReviewSeverity
{
    Critical = 0,
    Major = 1,
    Minor = 2,
    Info = 3
}

public sealed class ReviewRun
{
    public Guid Id { get; init; }

    public Guid CodingRunId { get; init; }

    public string? BaseRevision { get; init; }

    public string? HeadRevision { get; init; }

    public ReviewStatus Status { get; set; }

    public ReviewConclusion? Conclusion { get; set; }

    public string? Summary { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public int AttemptCount { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; init; } = [];
}

public sealed class ReviewFinding
{
    public Guid Id { get; init; }

    public Guid ReviewRunId { get; init; }

    public int SortOrder { get; init; }

    public ReviewSeverity Severity { get; init; }

    public string FilePath { get; init; } = string.Empty;

    public int? StartLine { get; init; }

    public int? EndLine { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string Suggestion { get; init; } = string.Empty;
}
