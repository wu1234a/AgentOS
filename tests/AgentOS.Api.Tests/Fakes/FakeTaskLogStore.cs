using AgentOS.Application.Tasks;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>In-memory log store; records batches for assertions.</summary>
public sealed class FakeTaskLogStore : ITaskLogStore
{
    public List<TaskLogEntry> Entries { get; } = [];

    public Task AddAsync(
        Guid taskId,
        Guid attemptId,
        Guid workerId,
        IReadOnlyList<TaskLogEntry> entries,
        CancellationToken cancellationToken)
    {
        Entries.AddRange(entries);
        return Task.CompletedTask;
    }
}
