namespace AgentOS.Domain.Tasks;

/// <summary>Outcome of one execution attempt.</summary>
public enum AttemptStatus
{
    /// <summary>The worker claimed the task and is executing it.</summary>
    Running = 0,

    Succeeded = 1,

    Failed = 2,

    /// <summary>The worker stopped heartbeating and the lease expired.</summary>
    Lost = 3,

    Cancelled = 4
}

/// <summary>
/// One execution attempt of a task. Retries never overwrite an earlier attempt; each claim
/// creates a new row and (TaskId, AttemptNumber) is unique.
/// </summary>
public sealed class TaskAttempt
{
    public Guid Id { get; init; }

    public Guid TaskId { get; init; }

    public Guid WorkerId { get; init; }

    public int AttemptNumber { get; init; }

    public AttemptStatus Status { get; set; } = AttemptStatus.Running;

    /// <summary>Runner-side session identifier (for example a Codex session id).</summary>
    public string? ExternalSessionId { get; set; }

    public DateTime StartedAtUtc { get; init; }

    public DateTime? FinishedAtUtc { get; set; }

    public int? ExitCode { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public int? DurationMs { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; set; }
}
