namespace AgentOS.Application.Tasks;

/// <summary>Claim request. Capacity is clamped to <c>TaskQueue:ClaimBatchLimit</c>.</summary>
public sealed class ClaimTasksRequest
{
    public int? Capacity { get; init; }
}

public sealed class StartTaskRequest
{
    public Guid? AttemptId { get; init; }

    public string? LeaseToken { get; init; }

    public string? ExternalSessionId { get; init; }
}

public sealed class HeartbeatTaskRequest
{
    public Guid? AttemptId { get; init; }

    public string? LeaseToken { get; init; }
}

public sealed class CompleteTaskRequest
{
    public Guid? AttemptId { get; init; }

    public string? LeaseToken { get; init; }

    public int? ExitCode { get; init; }
}

public sealed class FailTaskRequest
{
    public Guid? AttemptId { get; init; }

    public string? LeaseToken { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public bool Retryable { get; init; } = true;

    public bool Cancelled { get; init; }
}
