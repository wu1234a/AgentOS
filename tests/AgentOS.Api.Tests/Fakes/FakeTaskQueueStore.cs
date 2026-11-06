using AgentOS.Application.Tasks;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>In-memory queue store for lifecycle tests.</summary>
public sealed class FakeTaskQueueStore : ITaskQueueStore
{
    public List<Worker> Workers { get; } = [];

    public List<AutomationTask> Tasks { get; } = [];

    public List<TaskAttempt> Attempts { get; } = [];

    public int SaveCalls { get; private set; }

    public Task<Worker?> FindWorkerByCodeAsync(string workerCode, CancellationToken cancellationToken)
        => Task.FromResult(Workers.FirstOrDefault(worker => worker.WorkerCode == workerCode));

    public Task<bool> TouchWorkerAsync(Guid workerId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var worker = Workers.FirstOrDefault(candidate => candidate.Id == workerId);
        if (worker is null)
        {
            return Task.FromResult(false);
        }

        worker.LastHeartbeatAtUtc = nowUtc;
        worker.Status = WorkerStatus.Online;
        worker.UpdatedAtUtc = nowUtc;
        return Task.FromResult(true);
    }

    public Task<AutomationTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken)
        => Task.FromResult(Tasks.FirstOrDefault(task => task.Id == taskId));

    public Task<TaskAttempt?> GetAttemptAsync(Guid attemptId, CancellationToken cancellationToken)
        => Task.FromResult(Attempts.FirstOrDefault(attempt => attempt.Id == attemptId));

    public Task<IReadOnlyList<AutomationTask>> LoadExpiredLeasesAsync(
        DateTime nowUtc,
        int limit,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<AutomationTask>>(
            Tasks.Where(task => task.Status == TaskStatus.Running
                                && task.LeaseExpiresAtUtc is not null
                                && task.LeaseExpiresAtUtc <= nowUtc)
                .Take(limit)
                .ToArray());

    public Task<IReadOnlyList<TaskAttempt>> LoadRunningAttemptsAsync(
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TaskAttempt>>(
            Attempts.Where(attempt => attempt.Status == AttemptStatus.Running && taskIds.Contains(attempt.TaskId))
                .ToArray());

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        SaveCalls++;
        return Task.CompletedTask;
    }
}

/// <summary>Claimer stub returning pre-built claims.</summary>
public sealed class FakeTaskClaimer : ITaskClaimer
{
    public List<ClaimedTask> Claims { get; } = [];

    public int Calls { get; private set; }

    public Task<IReadOnlyList<ClaimedTask>> ClaimAsync(
        Guid workerId,
        int capacity,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<ClaimedTask>>(Claims.Take(capacity).ToArray());
    }
}
