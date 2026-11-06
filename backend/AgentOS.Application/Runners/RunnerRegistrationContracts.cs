using AgentOS.Domain.Security;

namespace AgentOS.Application.Runners;

/// <summary>Registration/bootstrap configuration (section <c>Runner</c>).</summary>
public sealed class RunnerRegistrationOptions
{
    /// <summary>
    /// Shared bootstrap key required by <c>POST /api/v1/runners/register</c>. When empty,
    /// registration is disabled and only pre-provisioned clients can connect.
    /// </summary>
    public string? RegistrationKey { get; set; }
}

/// <summary>Metadata a runner presents at registration.</summary>
public sealed record RunnerRegistrationRequest(
    string WorkerCode,
    string DisplayName,
    string HostName,
    string Platform,
    string Architecture,
    string Version,
    string CapabilitiesJson);

public enum RunnerRegistrationOutcome
{
    Registered = 0,
    Disabled = 1,
    Unauthorized = 2,
    Invalid = 3
}

public sealed record RunnerRegistrationResult(
    RunnerRegistrationOutcome Outcome,
    Security.RunnerRegistration? Registration,
    IReadOnlyDictionary<string, string[]> Errors);

/// <summary>Persistence seam for runner registration (worker upsert + credential rotation).</summary>
public interface IRunnerRegistrationStore
{
    Task<Guid> UpsertWorkerAsync(
        RunnerRegistrationRequest request,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Disables previous runner credentials of the worker, then stores the new one.</summary>
    Task ReplaceRunnerClientAsync(
        Guid workerId,
        ApiClient client,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}
