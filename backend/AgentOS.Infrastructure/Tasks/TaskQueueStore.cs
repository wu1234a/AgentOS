using AgentOS.Application.Tasks;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Infrastructure.Tasks;

/// <summary>EF Core implementation of the queue lifecycle store.</summary>
public sealed class TaskQueueStore(AgentOSDbContext dbContext) : ITaskQueueStore
{
    public Task<Worker?> FindWorkerByCodeAsync(string workerCode, CancellationToken cancellationToken)
        => dbContext.Workers.FirstOrDefaultAsync(worker => worker.WorkerCode == workerCode, cancellationToken);

    public async Task<bool> TouchWorkerAsync(
        Guid workerId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var updated = await dbContext.Workers
            .Where(worker => worker.Id == workerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(worker => worker.LastHeartbeatAtUtc, nowUtc)
                    .SetProperty(worker => worker.Status, WorkerStatus.Online)
                    .SetProperty(worker => worker.UpdatedAtUtc, nowUtc),
                cancellationToken);

        return updated > 0;
    }

    public Task<AutomationTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken)
        => dbContext.Tasks.FirstOrDefaultAsync(task => task.Id == taskId, cancellationToken);

    public Task<TaskAttempt?> GetAttemptAsync(Guid attemptId, CancellationToken cancellationToken)
        => dbContext.TaskAttempts.FirstOrDefaultAsync(attempt => attempt.Id == attemptId, cancellationToken);

    public async Task<IReadOnlyList<AutomationTask>> LoadExpiredLeasesAsync(
        DateTime nowUtc,
        int limit,
        CancellationToken cancellationToken)
        => await dbContext.Tasks
            .Where(task => task.Status == TaskStatus.Running
                           && task.LeaseExpiresAtUtc != null
                           && task.LeaseExpiresAtUtc <= nowUtc)
            .OrderBy(task => task.LeaseExpiresAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskAttempt>> LoadRunningAttemptsAsync(
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        var ids = taskIds.ToArray();
        return await dbContext.TaskAttempts
            .Where(attempt => attempt.Status == AttemptStatus.Running && ids.Contains(attempt.TaskId))
            .ToListAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
