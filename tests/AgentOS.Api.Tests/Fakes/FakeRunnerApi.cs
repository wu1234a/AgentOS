using AgentOS.Application.Tasks;
using AgentOS.Domain.Security;
using AgentOS.Runner;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>Records runner API calls and returns scripted responses.</summary>
public sealed class FakeRunnerApi : IRunnerApi
{
    public List<Guid> Started { get; } = [];

    public List<Guid> Completed { get; } = [];

    public List<(Guid TaskId, string Code, bool Retryable, bool Cancelled)> Failed { get; } = [];

    public List<TaskLogEntry[]> LogBatches { get; } = [];

    public List<(Guid TaskId, string Token)> Heartbeats { get; } = [];

    public RunnerRegistrationResponse Registration { get; set; } =
        new(Guid.NewGuid(), "runner-local", "secret-value", ["tasks:claim"], DateTime.UtcNow);

    /// <summary>When set, the next claim returns these tasks.</summary>
    public List<ClaimedTask> Claimed { get; } = [];

    public bool CancelOnHeartbeat { get; set; }

    public Exception? ClaimException { get; set; }

    /// <summary>Number of claim calls that fail with <see cref="ClaimException"/> before succeeding.</summary>
    public int ClaimFailuresBeforeSuccess { get; set; }

    public Exception? CompleteException { get; set; }

    public Task<RunnerRegistrationResponse> RegisterAsync(string registrationKey, CancellationToken cancellationToken)
        => Task.FromResult(Registration);

    public Task HeartbeatWorkerAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<ClaimedTask>> ClaimAsync(int capacity, CancellationToken cancellationToken)
    {
        if (ClaimFailuresBeforeSuccess > 0)
        {
            ClaimFailuresBeforeSuccess--;
            return Task.FromException<IReadOnlyList<ClaimedTask>>(
                ClaimException ?? new RunnerApiException(503, "unavailable", "down"));
        }

        var batch = Claimed.Take(capacity).ToArray();
        Claimed.RemoveRange(0, batch.Length);
        return Task.FromResult<IReadOnlyList<ClaimedTask>>(batch);
    }

    public Task<RunnerTaskResponse> StartAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        string? externalSessionId,
        CancellationToken cancellationToken)
    {
        Started.Add(taskId);
        return Task.FromResult(Response(taskId, "Running"));
    }

    public Task<RunnerTaskResponse> HeartbeatTaskAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        Heartbeats.Add((taskId, leaseToken));
        return Task.FromResult(Response(taskId, "Running", cancelRequested: CancelOnHeartbeat));
    }

    public Task<RunnerTaskResponse> CompleteAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        int exitCode,
        CancellationToken cancellationToken)
    {
        if (CompleteException is not null)
        {
            return Task.FromException<RunnerTaskResponse>(CompleteException);
        }

        Completed.Add(taskId);
        return Task.FromResult(Response(taskId, "Succeeded"));
    }

    public Task<RunnerTaskResponse> FailAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        string errorCode,
        string errorMessage,
        bool retryable,
        bool cancelled,
        CancellationToken cancellationToken)
    {
        Failed.Add((taskId, errorCode, retryable, cancelled));
        return Task.FromResult(Response(taskId, cancelled ? "Cancelled" : "Failed"));
    }

    public Task AppendLogsAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        IReadOnlyList<TaskLogEntry> entries,
        CancellationToken cancellationToken)
    {
        LogBatches.Add(entries.ToArray());
        return Task.CompletedTask;
    }

    private static RunnerTaskResponse Response(
        Guid taskId,
        string status,
        bool cancelRequested = false)
        => new(taskId, status, 1, status == "Running" ? "Running" : status, cancelRequested, null, null, null);
}

/// <summary>Scripted handler: succeeds, fails or blocks until cancelled.</summary>
public sealed class FakeTaskHandler : AgentOS.Runner.Handlers.ITaskHandler
{
    public string Name => "fake";

    public bool CancelUntilCancelled { get; set; }

    public bool Fail { get; set; }

    public List<string> Logs { get; } = [];

    public async Task<AgentOS.Runner.Handlers.TaskHandlerResult> ExecuteAsync(
        AgentOS.Runner.Handlers.TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        context.Log("info", "fake handler started");
        Logs.Add("started");

        if (CancelUntilCancelled)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                context.Log("warn", "fake handler cancelled");
                return AgentOS.Runner.Handlers.TaskHandlerResult.Cancelled();
            }
        }

        return Fail
            ? AgentOS.Runner.Handlers.TaskHandlerResult.Failed("fake-error", "fake failure")
            : AgentOS.Runner.Handlers.TaskHandlerResult.Succeeded();
    }
}

/// <summary>In-memory API client store for registration and client-auth tests.</summary>
public sealed class FakeApiClientStore : AgentOS.Application.Security.IApiClientStore
{
    public List<ApiClient> Clients { get; } = [];

    public Task<ApiClient?> FindByCodeAsync(string clientCode, CancellationToken cancellationToken)
        => Task.FromResult(Clients.FirstOrDefault(client => client.ClientCode == clientCode));

    public Task TouchAsync(Guid clientId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var client = Clients.FirstOrDefault(candidate => candidate.Id == clientId);
        if (client is not null)
        {
            client.LastUsedAtUtc = nowUtc;
        }

        return Task.CompletedTask;
    }
}
