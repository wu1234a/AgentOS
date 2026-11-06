using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Application.Tasks;

/// <summary>
/// Recovers tasks whose worker stopped heartbeating. The attempt is marked Lost (never
/// overwritten), the task is requeued with exponential backoff while attempts remain, and
/// otherwise becomes Failed.
/// </summary>
public sealed class LeaseRecoveryService(
    ITaskQueueStore store,
    TimeProvider timeProvider,
    IOptions<TaskQueueOptions> options,
    ILogger<LeaseRecoveryService> logger)
{
    private const int BatchLimit = 200;

    public async Task<LeaseRecoveryStats> RecoverAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expired = await store.LoadExpiredLeasesAsync(now, BatchLimit, cancellationToken);
        if (expired.Count == 0)
        {
            return new LeaseRecoveryStats(0, 0);
        }

        var attempts = await store.LoadRunningAttemptsAsync(
            expired.Select(task => task.Id).ToArray(),
            cancellationToken);

        var requeued = 0;
        var failed = 0;

        foreach (var task in expired)
        {
            var attempt = attempts.FirstOrDefault(candidate =>
                candidate.TaskId == task.Id && candidate.AttemptNumber == task.AttemptCount);

            if (attempt is not null)
            {
                attempt.Status = AttemptStatus.Lost;
                attempt.FinishedAtUtc = now;
                attempt.ErrorCode = "lease-expired";
                attempt.ErrorMessage = "Worker stopped heartbeating and the lease expired.";
                attempt.UpdatedAtUtc = now;
            }

            task.LeaseOwnerWorkerId = null;
            task.LeaseTokenHash = null;
            task.LeaseExpiresAtUtc = null;
            task.ErrorCode = "lease-expired";
            task.ErrorMessage = "Worker stopped heartbeating and the lease expired.";
            task.UpdatedAtUtc = now;

            if (task.AttemptCount < task.MaxAttempts)
            {
                task.Status = TaskStatus.RetryWait;
                task.NextRunAtUtc = TaskBackoff.Compute(now, task.AttemptCount, options.Value, task.Id);
                requeued++;
            }
            else
            {
                task.Status = TaskStatus.Failed;
                task.CompletedAtUtc = now;
                failed++;
            }
        }

        await store.SaveAsync(cancellationToken);

        logger.LogWarning(
            "Lease recovery: {Requeued} task(s) requeued with backoff, {Failed} task(s) failed after the last attempt.",
            requeued,
            failed);

        return new LeaseRecoveryStats(requeued, failed);
    }
}
