namespace AgentOS.Domain.Tasks;

/// <summary>
/// A unit of work produced by a rule match. Named <c>AutomationTask</c> to avoid colliding
/// with <see cref="System.Threading.Tasks.Task"/>; the database table is <c>Tasks</c>.
/// </summary>
public sealed class AutomationTask
{
    public Guid Id { get; init; }

    /// <summary>Human-readable identifier, unique.</summary>
    public string TaskCode { get; set; } = string.Empty;

    public Guid? EventId { get; init; }

    public Guid ProjectId { get; init; }

    public Guid? RuleId { get; init; }

    /// <summary>Rule revision that produced this task; exact traceability for replays.</summary>
    public int? RuleVersion { get; init; }

    public Guid AgentId { get; init; }

    public Guid? SkillId { get; init; }

    public Guid? ModelProfileId { get; init; }

    public Guid? ParentTaskId { get; init; }

    /// <summary>Stable phase key for Coding Run stage subtasks (AOS-016b).</summary>
    public string? PhaseKey { get; set; }

    /// <summary>
    /// Idempotency key: <c>EventId:RuleId:RuleVersion:DispatchKey</c>. Backed by a unique index,
    /// so the same event cannot produce the same logical task twice.
    /// </summary>
    public string LogicalTaskKey { get; init; } = string.Empty;

    public TaskStatus Status { get; set; } = TaskStatus.Queued;

    public string Priority { get; set; } = "normal";

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string InputJson { get; set; } = "{}";

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; }

    /// <summary>Number of automatic dead-letter recoveries (AOS-030).</summary>
    public int RecoveryCount { get; set; } = 2;

    public int TimeoutSeconds { get; set; } = 1800;

    public DateTime NextRunAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; set; }

    // ---- AOS-005 lease state -------------------------------------------------------------

    /// <summary>Worker that currently owns the lease, or null when the task is not running.</summary>
    public Guid? LeaseOwnerWorkerId { get; set; }

    /// <summary>SHA-256 (lowercase hex) of the lease token. The plaintext token is never stored.</summary>
    public string? LeaseTokenHash { get; set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }

    public DateTime? LastHeartbeatAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Set when a cancellation was requested while the task was running.</summary>
    public DateTime? CancelRequestedAtUtc { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Concurrency token used by the queue and lease operations (AOS-005).</summary>
    public byte[] RowVersion { get; init; } = [];
}
