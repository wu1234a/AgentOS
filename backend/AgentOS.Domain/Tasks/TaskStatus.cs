namespace AgentOS.Domain.Tasks;

/// <summary>Lifecycle of a task. AOS-004 creates <see cref="Queued"/> tasks only.</summary>
public enum TaskStatus
{
    Pending = 0,
    Queued = 1,
    Running = 2,
    RetryWait = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6,
    AwaitingApproval = 7,
    Rejected = 8,

    /// <summary>Waiting for all prerequisites to succeed (AOS-016).</summary>
    Blocked = 9
}
