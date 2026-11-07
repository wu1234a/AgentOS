using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Operations;

public sealed record DeadLetterItem(
    Guid Id,
    string TaskCode,
    string Title,
    Guid ProjectId,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    int RecoveryCount,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime UpdatedAtUtc);

public sealed class DeadLetterService(
    AgentOSDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<DeadLetterService> logger)
{
    private static readonly TaskStatus[] DeadLetterStatuses =
        [TaskStatus.Failed, TaskStatus.Blocked, TaskStatus.Rejected];

    public async Task<IReadOnlyList<DeadLetterItem>> ListAsync(
        Guid? projectId,
        string? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Tasks.AsNoTracking()
            .Where(task => DeadLetterStatuses.Contains(task.Status));
        if (projectId is Guid id)
        {
            query = query.Where(task => task.ProjectId == id);
        }

        if (Enum.TryParse<TaskStatus>(status, true, out var parsed) && DeadLetterStatuses.Contains(parsed))
        {
            query = query.Where(task => task.Status == parsed);
        }

        var rows = await query.OrderByDescending(task => task.UpdatedAtUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
        return rows.Select(task => new DeadLetterItem(
            task.Id, task.TaskCode, task.Title, task.ProjectId, task.Status.ToString(),
            task.AttemptCount, task.MaxAttempts, task.RecoveryCount, task.ErrorCode, task.ErrorMessage,
            task.UpdatedAtUtc)).ToList();
    }

    public async Task<bool> RetryAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await dbContext.Tasks.FirstOrDefaultAsync(candidate => candidate.Id == taskId, cancellationToken);
        if (task is null || !DeadLetterStatuses.Contains(task.Status))
        {
            return false;
        }

        Requeue(task);
        task.RecoveryCount++;
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Dead-letter task {TaskId} manually requeued.", taskId);
        return true;
    }

    public async Task<bool> CancelAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await dbContext.Tasks.FirstOrDefaultAsync(candidate => candidate.Id == taskId, cancellationToken);
        if (task is null || task.Status is TaskStatus.Succeeded or TaskStatus.Cancelled)
        {
            return false;
        }

        task.Status = TaskStatus.Cancelled;
        task.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        task.LeaseOwnerWorkerId = null;
        task.LeaseTokenHash = null;
        task.LeaseExpiresAtUtc = null;
        task.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> AutoRecoverAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now.AddMinutes(-15);
        var candidates = await dbContext.Tasks
            .Where(task => task.Status == TaskStatus.Failed
                && task.RecoveryCount < 3
                && task.UpdatedAtUtc < cutoff)
            .OrderBy(task => task.UpdatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var task in candidates)
        {
            Requeue(task);
            task.RecoveryCount++;
        }

        if (candidates.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Auto-recovered {Count} dead-letter tasks.", candidates.Count);
        }

        return candidates.Count;
    }

    private void Requeue(Domain.Tasks.AutomationTask task)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        task.Status = TaskStatus.Queued;
        task.AttemptCount = 0;
        task.NextRunAtUtc = now;
        task.LeaseOwnerWorkerId = null;
        task.LeaseTokenHash = null;
        task.LeaseExpiresAtUtc = null;
        task.CompletedAtUtc = null;
        task.ErrorCode = null;
        task.ErrorMessage = null;
        task.UpdatedAtUtc = now;
    }
}
