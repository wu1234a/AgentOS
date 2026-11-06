namespace AgentOS.Domain.Tasks;

/// <summary>
/// One log line emitted by a Runner for a task attempt. Large stdout/stderr stays in artifacts;
/// this table stores the structured, queryable stream.
/// </summary>
public sealed class TaskLog
{
    public Guid Id { get; init; }

    public Guid? ProjectId { get; init; }

    public Guid? EventId { get; init; }

    public Guid TaskId { get; init; }

    public Guid AttemptId { get; init; }

    public Guid WorkerId { get; init; }

    /// <summary>Monotonic sequence within an attempt; makes batch uploads idempotent.</summary>
    public int Sequence { get; init; }

    public string Level { get; init; } = "info";

    /// <summary>Producer, for example <c>runner</c>, <c>adapter</c>, <c>api</c>.</summary>
    public string Component { get; init; } = "runner";

    public string Message { get; init; } = string.Empty;

    /// <summary>Optional JSON object with structured fields.</summary>
    public string? PropertiesJson { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
