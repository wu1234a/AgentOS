using System.Text.Json;
using System.Text.RegularExpressions;
using AgentOS.Domain.ModelProfiles;

namespace AgentOS.Application.ModelProfiles;

/// <summary>Model/Provider profile registry and task/rule/agent resolution.</summary>
public sealed class ModelProfileService(IModelProfileRepository repository, TimeProvider timeProvider)
{
    private static readonly Regex CodePattern = new(
        "^[a-z0-9][a-z0-9-]{2,79}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SecretReferencePattern = new(
        "^[A-Z][A-Z0-9_]{2,80}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ForbiddenConfigurationKeys =
        ["api_key", "apikey", "secret", "password", "access_token", "client_secret"];

    public async Task<(IReadOnlyList<ModelProfileView> Items, int Total)> ListAsync(
        bool? enabled,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var (items, total) = await repository.ListAsync(enabled, limit, offset, cancellationToken);
        return (items.Select(ToView).ToList(), total);
    }

    public async Task<ModelProfileView?> GetAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await repository.GetAsync(profileId, cancellationToken);
        return profile is null ? null : ToView(profile);
    }

    public async Task<ModelProfileResult> CreateAsync(
        CreateModelProfileRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var code = NormalizeCode(request.Code, errors);

        var values = Normalize(
            request.Provider,
            request.Model,
            request.ExecutionMode,
            request.ConfigurationJson,
            request.SecretReference,
            request.TimeoutSeconds,
            request.MaxCostPerTask,
            errors);

        if (errors.Count > 0)
        {
            return ModelProfileResult.Invalid(errors);
        }

        if (await repository.CodeExistsAsync(code!, null, cancellationToken))
        {
            return ModelProfileResult.Conflict("model-profile-code-conflict");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var profile = new ModelProfile
        {
            Id = Guid.CreateVersion7(),
            Code = code!,
            Provider = values.Provider,
            Model = values.Model,
            ExecutionMode = values.ExecutionMode,
            ConfigurationJson = values.ConfigurationJson,
            SecretReference = values.SecretReference,
            TimeoutSeconds = values.TimeoutSeconds,
            MaxCostPerTask = values.MaxCostPerTask,
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (!await repository.TryAddAsync(profile, cancellationToken))
        {
            return ModelProfileResult.Conflict("model-profile-code-conflict");
        }

        return ModelProfileResult.Ok(ToView(profile));
    }

    public async Task<ModelProfileResult> UpdateAsync(
        Guid profileId,
        UpdateModelProfileRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await repository.GetAsync(profileId, cancellationToken);
        if (profile is null)
        {
            return ModelProfileResult.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var values = Normalize(
            request.Provider,
            request.Model,
            request.ExecutionMode,
            request.ConfigurationJson,
            request.SecretReference,
            request.TimeoutSeconds,
            request.MaxCostPerTask,
            errors);

        if (errors.Count > 0)
        {
            return ModelProfileResult.Invalid(errors);
        }

        profile.Provider = values.Provider;
        profile.Model = values.Model;
        profile.ExecutionMode = values.ExecutionMode;
        profile.ConfigurationJson = values.ConfigurationJson;
        profile.SecretReference = values.SecretReference;
        profile.TimeoutSeconds = values.TimeoutSeconds;
        profile.MaxCostPerTask = values.MaxCostPerTask;
        profile.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await repository.SaveAsync(cancellationToken);
        return ModelProfileResult.Ok(ToView(profile));
    }

    public Task<ModelProfileResult> EnableAsync(Guid profileId, CancellationToken cancellationToken)
        => SetEnabledAsync(profileId, true, cancellationToken);

    public Task<ModelProfileResult> DisableAsync(Guid profileId, CancellationToken cancellationToken)
        => SetEnabledAsync(profileId, false, cancellationToken);

    public Task<ResolvedModelProfile?> ResolveAsync(
        Guid? taskId,
        Guid? ruleId,
        Guid? agentId,
        CancellationToken cancellationToken)
        => repository.ResolveAsync(taskId, ruleId, agentId, cancellationToken);

    private async Task<ModelProfileResult> SetEnabledAsync(
        Guid profileId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var profile = await repository.GetAsync(profileId, cancellationToken);
        if (profile is null)
        {
            return ModelProfileResult.NotFound();
        }

        if (profile.IsEnabled != enabled)
        {
            profile.IsEnabled = enabled;
            profile.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await repository.SaveAsync(cancellationToken);
        }

        return ModelProfileResult.Ok(ToView(profile));
    }

    private static NormalizedProfile Normalize(
        string? provider,
        string? model,
        string? executionMode,
        string? configurationJson,
        string? secretReference,
        int? timeoutSeconds,
        decimal? maxCostPerTask,
        Dictionary<string, string[]> errors)
    {
        var normalizedProvider = provider?.Trim() ?? string.Empty;
        if (normalizedProvider.Length is 0 or > 60)
        {
            errors["provider"] = ["provider is required and must be 60 characters or fewer."];
        }

        var normalizedModel = model?.Trim() ?? string.Empty;
        if (normalizedModel.Length is 0 or > 120)
        {
            errors["model"] = ["model is required and must be 120 characters or fewer."];
        }

        var normalizedMode = executionMode?.Trim().ToLowerInvariant() ?? "cli";
        if (normalizedMode is not ("cli" or "api"))
        {
            errors["executionMode"] = ["executionMode must be cli or api."];
        }

        var normalizedConfiguration = string.IsNullOrWhiteSpace(configurationJson) ? "{}" : configurationJson.Trim();
        if (normalizedConfiguration.Length > 8000)
        {
            errors["configurationJson"] = ["configurationJson must be 8000 characters or fewer."];
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors["configurationJson"] = ["configurationJson must be a JSON object."];
                }
                else
                {
                    if (ContainsForbiddenSecret(document.RootElement))
                    {
                        errors["configurationJson"] = ["configurationJson must not contain secret fields; use secretReference."];
                    }
                }
            }
            catch (JsonException)
            {
                errors["configurationJson"] = ["configurationJson must be valid JSON."];
            }
        }

        var normalizedSecret = string.IsNullOrWhiteSpace(secretReference) ? null : secretReference.Trim();
        if (normalizedSecret is not null && !SecretReferencePattern.IsMatch(normalizedSecret))
        {
            errors["secretReference"] = ["secretReference must be an environment variable name, not a secret value."];
        }

        var normalizedTimeout = timeoutSeconds ?? 1800;
        if (normalizedTimeout is < 1 or > 7200)
        {
            errors["timeoutSeconds"] = ["timeoutSeconds must be between 1 and 7200."];
        }

        if (maxCostPerTask is < 0)
        {
            errors["maxCostPerTask"] = ["maxCostPerTask must not be negative."];
        }

        return new NormalizedProfile(
            normalizedProvider,
            normalizedModel,
            normalizedMode,
            normalizedConfiguration,
            normalizedSecret,
            normalizedTimeout,
            maxCostPerTask);
    }

    private static bool ContainsForbiddenSecret(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (ForbiddenConfigurationKeys.Contains(property.Name.ToLowerInvariant())
                    || ContainsForbiddenSecret(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsForbiddenSecret(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? NormalizeCode(string? code, Dictionary<string, string[]> errors)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !CodePattern.IsMatch(normalized))
        {
            errors["code"] = ["code must be a lowercase slug of 3-80 characters."];
            return null;
        }

        return normalized;
    }

    private static ModelProfileView ToView(ModelProfile profile)
        => new(
            profile.Id,
            profile.Code,
            profile.Provider,
            profile.Model,
            profile.ExecutionMode,
            profile.ConfigurationJson,
            profile.SecretReference,
            profile.TimeoutSeconds,
            profile.MaxCostPerTask,
            profile.IsEnabled,
            profile.CreatedAtUtc,
            profile.UpdatedAtUtc);

    private sealed record NormalizedProfile(
        string Provider,
        string Model,
        string ExecutionMode,
        string ConfigurationJson,
        string? SecretReference,
        int TimeoutSeconds,
        decimal? MaxCostPerTask);
}
