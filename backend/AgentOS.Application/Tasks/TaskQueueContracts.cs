using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;

namespace AgentOS.Application.Tasks;

/// <summary>Lease returned to the Runner once, right after a successful claim.</summary>
public sealed record TaskLease(
    Guid AttemptId,
    int AttemptNumber,
    string Token,
    DateTime ExpiresAtUtc);

/// <summary>Task details the Runner needs to execute the work.</summary>
public sealed record ClaimedTask(
    Guid TaskId,
    string ProjectSlug,
    string TaskCode,
    string Title,
    string Priority,
    string InputJson,
    Guid? RuleId,
    int? RuleVersion,
    Guid AgentId,
    Guid? SkillId,
    Guid? ModelProfileId,
    int MaxAttempts,
    int TimeoutSeconds,
    TaskLease Lease);

/// <summary>Atomically claims due tasks for one worker.</summary>
public interface ITaskClaimer
{
    Task<IReadOnlyList<ClaimedTask>> ClaimAsync(Guid workerId, int capacity, CancellationToken cancellationToken);
}

/// <summary>Outcome categories of a queue operation.</summary>
public enum TaskQueueOutcome
{
    Ok = 0,

    /// <summary>The task or attempt does not exist.</summary>
    NotFound = 1,

    /// <summary>The worker does not own this lease, the token is wrong, or the lease expired.</summary>
    Forbidden = 2,

    /// <summary>The task is in a state that cannot accept this operation.</summary>
    Conflict = 3
}

/// <summary>Result of a lifecycle operation; <c>Error</c> is a stable machine-readable code.</summary>
public sealed record TaskQueueResult(
    TaskQueueOutcome Outcome,
    AutomationTask? Task,
    TaskAttempt? Attempt,
    DateTime? LeaseExpiresAtUtc,
    bool CancelRequested,
    string? Error)
{
    public static TaskQueueResult Ok(
        AutomationTask task,
        TaskAttempt? attempt = null,
        DateTime? leaseExpiresAtUtc = null,
        bool cancelRequested = false)
        => new(TaskQueueOutcome.Ok, task, attempt, leaseExpiresAtUtc, cancelRequested, null);

    public static TaskQueueResult Failure(TaskQueueOutcome outcome, string error)
        => new(outcome, null, null, null, false, error);
}

/// <summary>Persistence seam for queue lifecycle operations.</summary>
public interface ITaskQueueStore
{
    Task<Worker?> FindWorkerByCodeAsync(string workerCode, CancellationToken cancellationToken);

    Task<Worker?> FindWorkerByIdAsync(Guid workerId, CancellationToken cancellationToken);

    /// <summary>Updates the worker's last-seen time and marks it online.</summary>
    Task<bool> TouchWorkerAsync(Guid workerId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Tracked task entity.</summary>
    Task<AutomationTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken);

    Task<TaskAttempt?> GetAttemptAsync(Guid attemptId, CancellationToken cancellationToken);

    /// <summary>Running tasks whose lease expired at or before <paramref name="nowUtc"/>.</summary>
    Task<IReadOnlyList<AutomationTask>> LoadExpiredLeasesAsync(
        DateTime nowUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskAttempt>> LoadRunningAttemptsAsync(
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>Result of one lease-recovery pass.</summary>
public sealed record LeaseRecoveryStats(int Requeued, int Failed, int Cancelled);
