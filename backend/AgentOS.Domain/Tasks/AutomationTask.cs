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

    /// <summary>
    /// Idempotency key: <c>EventId:RuleId:RuleVersion:DispatchKey</c>. Backed by a unique index,
    /// so the same event cannot produce the same logical task twice.
    /// </summary>
    public string LogicalTaskKey { get; init; } = string.Empty;

    public TaskStatus Status { get; init; } = TaskStatus.Queued;

    public string Priority { get; set; } = "normal";

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string InputJson { get; set; } = "{}";

    public int AttemptCount { get; init; }

    public int MaxAttempts { get; set; } = 2;

    public int TimeoutSeconds { get; set; } = 1800;

    public DateTime NextRunAtUtc { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Concurrency token used by the queue and lease operations (AOS-005).</summary>
    public byte[] RowVersion { get; init; } = [];
}
