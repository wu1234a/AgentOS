using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Application.Tasks;

/// <summary>
/// Recovers tasks whose worker stopped heartbeating. The attempt is marked Lost (never
/// overwritten), the task is requeued with exponential backoff while attempts remain, and
/// otherwise becomes Failed. A task with a pending cancellation request is finalized as
/// Cancelled instead of being executed again.
/// </summary>
public sealed class LeaseRecoveryService(
    ITaskQueueStore store,
    TimeProvider timeProvider,
    IOptions<TaskQueueOptions> options,
    ILogger<LeaseRecoveryService> logger,
    ITaskDependencyCoordinator? dependencies = null)
{
    private const int BatchLimit = 200;

    public async Task<LeaseRecoveryStats> RecoverAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (dependencies is not null)
        {
            await dependencies.ReleaseAndPropagateAsync(now, cancellationToken);
        }
        var expired = await store.LoadExpiredLeasesAsync(now, BatchLimit, cancellationToken);
        if (expired.Count == 0)
        {
            return new LeaseRecoveryStats(0, 0, 0);
        }

        var attempts = await store.LoadRunningAttemptsAsync(
            expired.Select(task => task.Id).ToArray(),
            cancellationToken);

        var requeued = 0;
        var failed = 0;
        var cancelled = 0;

        foreach (var task in expired)
        {
            var attempt = attempts.FirstOrDefault(candidate =>
                candidate.TaskId == task.Id && candidate.AttemptNumber == task.AttemptCount);
            var isCancelled = task.CancelRequestedAtUtc is not null;

            if (attempt is not null)
            {
                attempt.Status = isCancelled ? AttemptStatus.Cancelled : AttemptStatus.Lost;
                attempt.FinishedAtUtc = now;
                attempt.ErrorCode = isCancelled ? "cancelled" : "lease-expired";
                attempt.ErrorMessage = isCancelled
                    ? "Cancellation was requested and the worker stopped heartbeating."
                    : "Worker stopped heartbeating and the lease expired.";
                attempt.UpdatedAtUtc = now;
            }

            task.LeaseOwnerWorkerId = null;
            task.LeaseTokenHash = null;
            task.LeaseExpiresAtUtc = null;
            task.UpdatedAtUtc = now;

            if (isCancelled)
            {
                task.Status = TaskStatus.Cancelled;
                task.CompletedAtUtc = now;
                task.ErrorCode = "cancelled";
                task.ErrorMessage = "Cancellation was requested and the worker stopped heartbeating.";
                cancelled++;
                continue;
            }

            task.ErrorCode = "lease-expired";
            task.ErrorMessage = "Worker stopped heartbeating and the lease expired.";

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
            "Lease recovery: {Requeued} requeued, {Failed} failed, {Cancelled} cancelled.",
            requeued,
            failed,
            cancelled);

        return new LeaseRecoveryStats(requeued, failed, cancelled);
    }
}
