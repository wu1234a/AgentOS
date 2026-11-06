namespace AgentOS.Domain.Tasks;

/// <summary>
/// One log line emitted by a Runner for a task attempt. Large logs stay in files; this table
/// stores the structured, queryable stream (AOS-008 builds the UI on top).
/// </summary>
public sealed class TaskLog
{
    public Guid Id { get; init; }

    public Guid TaskId { get; init; }

    public Guid AttemptId { get; init; }

    public Guid WorkerId { get; init; }

    /// <summary>Monotonic sequence within an attempt; makes batch uploads idempotent.</summary>
    public int Sequence { get; init; }

    public string Level { get; init; } = "info";

    public string Message { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }
}
