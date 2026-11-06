namespace AgentOS.Domain.CodingRuns;

public enum CodingRunStatus
{
    Queued = 0,
    Preparing = 1,
    Running = 2,
    Validating = 3,
    Reporting = 4,
    Succeeded = 5,
    Failed = 6,
    RetryWait = 7,
    Canceling = 8,
    Canceled = 9
}

/// <summary>
/// A Coding Run is the durable orchestration record for one approved Planner draft. It owns an
/// immutable snapshot and exactly one executable task at a time; retries create a new task while
/// preserving the run history.
/// </summary>
public sealed class CodingRun
{
    public Guid Id { get; init; }

    public Guid DraftId { get; init; }

    public string DraftContentHash { get; init; } = string.Empty;

    public Guid ProjectId { get; init; }

    public Guid AgentId { get; init; }

    public Guid? TaskId { get; set; }

    public CodingRunStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public string SnapshotJson { get; init; } = "{}";

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime? CancelRequestedAtUtc { get; set; }

    public byte[] RowVersion { get; init; } = [];
}

/// <summary>Audit trail of Coding Run status changes and operator actions.</summary>
public sealed class CodingRunEvent
{
    public Guid Id { get; init; }

    public Guid CodingRunId { get; init; }

    public int Sequence { get; init; }

    public string EventType { get; init; } = string.Empty;

    public string? FromStatus { get; init; }

    public string ToStatus { get; init; } = string.Empty;

    public string? DetailJson { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
