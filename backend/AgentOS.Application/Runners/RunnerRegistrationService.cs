using System.Text.RegularExpressions;
using AgentOS.Application.Security;
using AgentOS.Domain.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOS.Application.Runners;

/// <summary>
/// Registers a runner: validates the bootstrap key, upserts the worker and issues a fresh
/// runner-scoped API client. The plaintext secret is returned once and never stored.
/// </summary>
public sealed class RunnerRegistrationService(
    IRunnerRegistrationStore store,
    IOptions<RunnerRegistrationOptions> options,
    TimeProvider timeProvider,
    ILogger<RunnerRegistrationService> logger)
{
    public const string ClaimScope = "tasks:claim";
    public const string ReportScope = "tasks:report";
    public const string LogScope = "tasks:log";

    public static readonly string[] RunnerScopes = [ClaimScope, ReportScope, LogScope];

    private static readonly Regex WorkerCodePattern = new(
        "^[a-z0-9][a-z0-9-]{2,79}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.RegistrationKey);

    public async Task<RunnerRegistrationResult> RegisterAsync(
        string? providedKey,
        RunnerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return new RunnerRegistrationResult(
                RunnerRegistrationOutcome.Disabled,
                null,
                new Dictionary<string, string[]>());
        }

        if (!ClientSecretHasher.FixedTimeEquals(options.Value.RegistrationKey, providedKey))
        {
            return new RunnerRegistrationResult(
                RunnerRegistrationOutcome.Unauthorized,
                null,
                new Dictionary<string, string[]>());
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.WorkerCode) || !WorkerCodePattern.IsMatch(request.WorkerCode.Trim()))
        {
            errors["workerCode"] = ["workerCode must be a lowercase slug of 3-80 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 120)
        {
            errors["displayName"] = ["displayName is required and must be 120 characters or fewer."];
        }

        if (string.IsNullOrWhiteSpace(request.HostName) || request.HostName.Length > 200)
        {
            errors["hostName"] = ["hostName is required and must be 200 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return new RunnerRegistrationResult(RunnerRegistrationOutcome.Invalid, null, errors);
        }

        var normalized = request with
        {
            WorkerCode = request.WorkerCode.Trim(),
            DisplayName = request.DisplayName.Trim(),
            HostName = request.HostName.Trim(),
            Platform = string.IsNullOrWhiteSpace(request.Platform) ? "Unknown" : request.Platform.Trim(),
            Architecture = string.IsNullOrWhiteSpace(request.Architecture) ? "Unknown" : request.Architecture.Trim(),
            Version = string.IsNullOrWhiteSpace(request.Version) ? "0.0.0" : request.Version.Trim(),
            CapabilitiesJson = string.IsNullOrWhiteSpace(request.CapabilitiesJson) ? "{}" : request.CapabilitiesJson
        };

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var workerId = await store.UpsertWorkerAsync(normalized, now, cancellationToken);

        var secret = ClientSecretHasher.CreateSecret();
        var client = new ApiClient
        {
            Id = Guid.CreateVersion7(),
            ClientCode = $"runner-{normalized.WorkerCode}",
            ClientType = ApiClientType.Runner,
            WorkerId = workerId,
            SecretHash = ClientSecretHasher.Hash(secret),
            ScopesJson = System.Text.Json.JsonSerializer.Serialize(RunnerScopes),
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await store.ReplaceRunnerClientAsync(workerId, client, now, cancellationToken);

        logger.LogInformation(
            "Runner {WorkerCode} registered as worker {WorkerId}; previous runner credentials were revoked.",
            normalized.WorkerCode,
            workerId);

        return new RunnerRegistrationResult(
            RunnerRegistrationOutcome.Registered,
            new Security.RunnerRegistration(workerId, client.ClientCode, secret, RunnerScopes, now),
            new Dictionary<string, string[]>());
    }
}
