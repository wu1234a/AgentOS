using AgentOS.Domain.ModelProfiles;

namespace AgentOS.Application.ModelProfiles;

public sealed record ModelProfileView(
    Guid Id,
    string Code,
    string Provider,
    string Model,
    string ExecutionMode,
    string ConfigurationJson,
    string? SecretReference,
    int TimeoutSeconds,
    decimal? MaxCostPerTask,
    bool IsEnabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ResolvedModelProfile(
    Guid Id,
    string Code,
    string Provider,
    string Model,
    string ExecutionMode,
    string ConfigurationJson,
    string? SecretReference,
    int TimeoutSeconds,
    string Source);

public sealed class CreateModelProfileRequest
{
    public string? Code { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? ExecutionMode { get; init; }

    public string? ConfigurationJson { get; init; }

    public string? SecretReference { get; init; }

    public int? TimeoutSeconds { get; init; }

    public decimal? MaxCostPerTask { get; init; }
}

public sealed class UpdateModelProfileRequest
{
    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? ExecutionMode { get; init; }

    public string? ConfigurationJson { get; init; }

    public string? SecretReference { get; init; }

    public int? TimeoutSeconds { get; init; }

    public decimal? MaxCostPerTask { get; init; }
}

public enum ModelProfileOutcome
{
    Ok = 0,
    NotFound = 1,
    Conflict = 2,
    ValidationFailed = 3
}

public sealed record ModelProfileResult(
    ModelProfileOutcome Outcome,
    ModelProfileView? Profile,
    string? ErrorCode,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static ModelProfileResult Ok(ModelProfileView profile)
        => new(ModelProfileOutcome.Ok, profile, null, Empty);

    public static ModelProfileResult NotFound()
        => new(ModelProfileOutcome.NotFound, null, "model-profile-not-found", Empty);

    public static ModelProfileResult Conflict(string code)
        => new(ModelProfileOutcome.Conflict, null, code, Empty);

    public static ModelProfileResult Invalid(IReadOnlyDictionary<string, string[]> errors)
        => new(ModelProfileOutcome.ValidationFailed, null, "model-profile-invalid", errors);

    private static readonly Dictionary<string, string[]> Empty = [];
}

public interface IModelProfileRepository
{
    Task<(IReadOnlyList<ModelProfile> Items, int Total)> ListAsync(
        bool? enabled,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<ModelProfile?> GetAsync(Guid profileId, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, Guid? excludeProfileId, CancellationToken cancellationToken);

    Task<bool> TryAddAsync(ModelProfile profile, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);

    Task<ResolvedModelProfile?> ResolveAsync(
        Guid? taskId,
        Guid? ruleId,
        Guid? agentId,
        CancellationToken cancellationToken);
}
