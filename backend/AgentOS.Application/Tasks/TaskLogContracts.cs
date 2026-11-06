namespace AgentOS.Application.Tasks;

/// <summary>One log line sent by a runner. Unknown levels are normalized to <c>info</c>.</summary>
public sealed record TaskLogEntry(
    int Sequence,
    string? Level,
    string? Message,
    DateTime? TimestampUtc);

/// <summary>Persists runner log lines idempotently by (attempt, sequence).</summary>
public interface ITaskLogStore
{
    Task AddAsync(
        Guid? projectId,
        Guid? eventId,
        Guid taskId,
        Guid attemptId,
        Guid workerId,
        IReadOnlyList<TaskLogEntry> entries,
        CancellationToken cancellationToken);
}
