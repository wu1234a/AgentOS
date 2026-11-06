namespace AgentOS.Application.Tasks;

/// <summary>
/// Queue and lease configuration. Every value is configurable under the <c>TaskQueue</c>
/// section; defaults follow docs/ARCHITECTURE.md §8.
/// </summary>
public sealed class TaskQueueOptions
{
    public int LeaseDurationSeconds { get; set; } = 90;

    public int HeartbeatIntervalSeconds { get; set; } = 20;

    public int TaskTimeoutSeconds { get; set; } = 1800;

    public int MaxAttempts { get; set; } = 2;

    /// <summary>Base of the exponential retry backoff, in seconds.</summary>
    public int BackoffBaseSeconds { get; set; } = 30;

    public int BackoffMaxSeconds { get; set; } = 900;

    /// <summary>Maximum number of tasks a single claim may take.</summary>
    public int ClaimBatchLimit { get; set; } = 10;

    /// <summary>How often the Worker host scans for expired leases.</summary>
    public int RecoveryIntervalSeconds { get; set; } = 30;

    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(LeaseDurationSeconds);
}
