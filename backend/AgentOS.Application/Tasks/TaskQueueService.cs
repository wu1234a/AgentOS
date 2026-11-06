using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Application.Tasks;

/// <summary>
/// Start / heartbeat / complete / fail / cancel lifecycle. Every mutation validates worker,
/// task, attempt and lease token; no operation trusts a task id alone.
/// </summary>
public sealed class TaskQueueService(
    ITaskQueueStore store,
    ITaskLogStore logStore,
    TimeProvider timeProvider,
    IOptions<TaskQueueOptions> options,
    ILogger<TaskQueueService> logger)
{
    private const int MaxErrorMessageLength = 2000;
    private const int MaxErrorCodeLength = 100;
    private const int MaxExternalSessionIdLength = 200;

    private TaskQueueOptions Options => options.Value;

    public async Task<TaskQueueResult> StartAsync(
        Guid workerId,
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        string? externalSessionId,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateLeaseAsync(workerId, taskId, attemptId, leaseToken, cancellationToken);
        if (validated.Failure is not null)
        {
            return validated.Failure;
        }

        var task = validated.Task!;
        var attempt = validated.Attempt!;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        attempt.ExternalSessionId = Truncate(TrimToNull(externalSessionId), MaxExternalSessionIdLength);
        attempt.UpdatedAtUtc = now;
        task.LastHeartbeatAtUtc = now;
        task.LeaseExpiresAtUtc = now.Add(Options.LeaseDuration);
        task.UpdatedAtUtc = now;

        await store.SaveAsync(cancellationToken);
        return TaskQueueResult.Ok(task, attempt, task.LeaseExpiresAtUtc, task.CancelRequestedAtUtc is not null);
    }

    public async Task<TaskQueueResult> HeartbeatAsync(
        Guid workerId,
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateLeaseAsync(workerId, taskId, attemptId, leaseToken, cancellationToken);
        if (validated.Failure is not null)
        {
            return validated.Failure;
        }

        var task = validated.Task!;
        var attempt = validated.Attempt!;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        task.LastHeartbeatAtUtc = now;
        task.LeaseExpiresAtUtc = now.Add(Options.LeaseDuration);
        task.UpdatedAtUtc = now;
        attempt.UpdatedAtUtc = now;

        await store.SaveAsync(cancellationToken);
        return TaskQueueResult.Ok(task, attempt, task.LeaseExpiresAtUtc, task.CancelRequestedAtUtc is not null);
    }

    public async Task<TaskQueueResult> CompleteAsync(
        Guid workerId,
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        int? exitCode,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateLeaseAsync(workerId, taskId, attemptId, leaseToken, cancellationToken);
        if (validated.Failure is not null)
        {
            return validated.Failure;
        }

        var task = validated.Task!;
        var attempt = validated.Attempt!;

        // A cancellation request must never be recorded as a success.
        if (task.CancelRequestedAtUtc is not null)
        {
            return TaskQueueResult.Failure(TaskQueueOutcome.Conflict, "cancel-requested");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        task.Status = TaskStatus.Succeeded;
        task.CompletedAtUtc = now;
        task.UpdatedAtUtc = now;
        ClearLease(task);

        attempt.Status = AttemptStatus.Succeeded;
        attempt.ExitCode = exitCode;
        FinishAttempt(attempt, now);

        await store.SaveAsync(cancellationToken);
        logger.LogInformation(
            "Task {TaskId} attempt {AttemptNumber} succeeded on worker {WorkerId}.",
            task.Id,
            attempt.AttemptNumber,
            workerId);

        return TaskQueueResult.Ok(task, attempt);
    }

    public async Task<TaskQueueResult> FailAsync(
        Guid workerId,
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        string? errorCode,
        string? errorMessage,
        bool retryable,
        bool cancelled,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateLeaseAsync(workerId, taskId, attemptId, leaseToken, cancellationToken);
        if (validated.Failure is not null)
        {
            return validated.Failure;
        }

        var task = validated.Task!;
        var attempt = validated.Attempt!;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // A cancellation request always wins: a worker that reports a plain failure after the
        // task was cancelled must not put cancelled work back into the queue.
        var effectiveCancelled = cancelled || task.CancelRequestedAtUtc is not null;

        attempt.ErrorCode = Truncate(TrimToNull(errorCode) ?? (effectiveCancelled ? "cancelled" : "failed"), MaxErrorCodeLength);
        attempt.ErrorMessage = Truncate(errorMessage);
        attempt.Status = effectiveCancelled ? AttemptStatus.Cancelled : AttemptStatus.Failed;
        FinishAttempt(attempt, now);

        task.ErrorCode = attempt.ErrorCode;
        task.ErrorMessage = attempt.ErrorMessage;
        task.UpdatedAtUtc = now;
        ClearLease(task);

        if (effectiveCancelled)
        {
            task.Status = TaskStatus.Cancelled;
            task.CompletedAtUtc = now;
        }
        else if (retryable && task.AttemptCount < task.MaxAttempts)
        {
            task.Status = TaskStatus.RetryWait;
            task.NextRunAtUtc = TaskBackoff.Compute(now, task.AttemptCount, Options, task.Id);
            logger.LogWarning(
                "Task {TaskId} attempt {AttemptNumber} failed; retry scheduled at {NextRunAtUtc} ({AttemptCount}/{MaxAttempts}).",
                task.Id,
                attempt.AttemptNumber,
                task.NextRunAtUtc,
                task.AttemptCount,
                task.MaxAttempts);
        }
        else
        {
            task.Status = TaskStatus.Failed;
            task.CompletedAtUtc = now;
        }

        await store.SaveAsync(cancellationToken);
        return TaskQueueResult.Ok(task, attempt);
    }

    /// <summary>
    /// Appends a batch of runner log lines. Requires a valid lease; entries are keyed by
    /// (attempt, sequence) so a retried batch cannot duplicate or reorder the stream.
    /// </summary>
    public async Task<TaskQueueResult> AppendLogsAsync(
        Guid workerId,
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        IReadOnlyList<TaskLogEntry> entries,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateLeaseAsync(workerId, taskId, attemptId, leaseToken, cancellationToken);
        if (validated.Failure is not null)
        {
            return validated.Failure;
        }

        if (entries.Count > 0)
        {
            await logStore.AddAsync(taskId, attemptId, workerId, entries, cancellationToken);
        }

        return TaskQueueResult.Ok(validated.Task!, validated.Attempt!, validated.Task!.LeaseExpiresAtUtc);
    }

    /// <summary>
    /// Administrative cancellation. Queued work is cancelled immediately; a running task gets a
    /// cancellation request that the worker observes through heartbeat.
    /// </summary>
    public async Task<TaskQueueResult> CancelAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await store.GetTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return TaskQueueResult.Failure(TaskQueueOutcome.NotFound, "task-not-found");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        switch (task.Status)
        {
            case TaskStatus.Pending:
            case TaskStatus.Queued:
            case TaskStatus.RetryWait:
                task.Status = TaskStatus.Cancelled;
                task.CompletedAtUtc = now;
                task.ErrorCode = "cancelled";
                task.ErrorMessage = "Cancelled before execution.";
                task.UpdatedAtUtc = now;
                ClearLease(task);
                await store.SaveAsync(cancellationToken);
                return TaskQueueResult.Ok(task);

            case TaskStatus.Running:
                if (task.CancelRequestedAtUtc is null)
                {
                    task.CancelRequestedAtUtc = now;
                    task.UpdatedAtUtc = now;
                    await store.SaveAsync(cancellationToken);
                    logger.LogInformation("Cancellation requested for running task {TaskId}.", task.Id);
                }

                return TaskQueueResult.Ok(task, cancelRequested: true);

            default:
                // Already terminal: cancellation is idempotent and must not rewrite the outcome.
                return TaskQueueResult.Ok(task);
        }
    }

    private async Task<(AutomationTask? Task, TaskAttempt? Attempt, TaskQueueResult? Failure)> ValidateLeaseAsync(
        Guid workerId,
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        var task = await store.GetTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return (null, null, TaskQueueResult.Failure(TaskQueueOutcome.NotFound, "task-not-found"));
        }

        if (task.Status != TaskStatus.Running)
        {
            return (null, null, TaskQueueResult.Failure(TaskQueueOutcome.Conflict, "task-not-running"));
        }

        if (task.LeaseOwnerWorkerId != workerId)
        {
            return (null, null, TaskQueueResult.Failure(TaskQueueOutcome.Forbidden, "worker-mismatch"));
        }

        if (!LeaseTokenService.Matches(task.LeaseTokenHash, leaseToken))
        {
            return (null, null, TaskQueueResult.Failure(TaskQueueOutcome.Forbidden, "invalid-lease-token"));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (task.LeaseExpiresAtUtc is null || task.LeaseExpiresAtUtc <= now)
        {
            return (null, null, TaskQueueResult.Failure(TaskQueueOutcome.Forbidden, "lease-expired"));
        }

        var attempt = await store.GetAttemptAsync(attemptId, cancellationToken);
        if (attempt is null
            || attempt.TaskId != taskId
            || attempt.WorkerId != workerId
            || attempt.AttemptNumber != task.AttemptCount
            || attempt.Status != AttemptStatus.Running)
        {
            return (null, null, TaskQueueResult.Failure(TaskQueueOutcome.Conflict, "attempt-mismatch"));
        }

        return (task, attempt, null);
    }

    private static void ClearLease(AutomationTask task)
    {
        task.LeaseOwnerWorkerId = null;
        task.LeaseTokenHash = null;
        task.LeaseExpiresAtUtc = null;
    }

    private void FinishAttempt(TaskAttempt attempt, DateTime nowUtc)
    {
        attempt.FinishedAtUtc = nowUtc;
        attempt.DurationMs = (int)Math.Max(0, (nowUtc - attempt.StartedAtUtc).TotalMilliseconds);
        attempt.UpdatedAtUtc = nowUtc;
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value)
        => Truncate(value, MaxErrorMessageLength);

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
