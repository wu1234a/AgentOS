using AgentOS.Application.Tasks;

namespace AgentOS.Runner;

/// <summary>Task lifecycle response returned by the API.</summary>
public sealed record RunnerTaskResponse(
    Guid TaskId,
    string Status,
    int? AttemptNumber,
    string? AttemptStatus,
    bool CancelRequested,
    DateTime? LeaseExpiresAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorCode);

public sealed record ClaimResponse(IReadOnlyList<ClaimedTask> Tasks);

public sealed record RunnerRegistrationResponse(
    Guid WorkerId,
    string ClientId,
    string ClientSecret,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAtUtc);

/// <summary>Failure returned by the API; <c>IsTransient</c> drives backoff decisions.</summary>
public sealed class RunnerApiException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;

    /// <summary>Server unavailable, throttled, timeout or a 5xx answer.</summary>
    public bool IsTransient => StatusCode is 0 or 408 or 429 or >= 500;
}

/// <summary>Outbound-only runner API surface; tests replace it with a fake.</summary>
public interface IRunnerApi
{
    Task<RunnerRegistrationResponse> RegisterAsync(
        string registrationKey,
        CancellationToken cancellationToken);

    Task HeartbeatWorkerAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ClaimedTask>> ClaimAsync(int capacity, CancellationToken cancellationToken);

    Task<RunnerTaskResponse> StartAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        string? externalSessionId,
        CancellationToken cancellationToken);

    Task<RunnerTaskResponse> HeartbeatTaskAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        CancellationToken cancellationToken);

    Task<RunnerTaskResponse> CompleteAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        int exitCode,
        CancellationToken cancellationToken);

    Task<RunnerTaskResponse> FailAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        string errorCode,
        string errorMessage,
        bool retryable,
        bool cancelled,
        CancellationToken cancellationToken);

    Task AppendLogsAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        IReadOnlyList<TaskLogEntry> entries,
        CancellationToken cancellationToken);
}
