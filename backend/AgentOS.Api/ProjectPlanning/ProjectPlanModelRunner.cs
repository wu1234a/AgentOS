using System.Text.Json;
using AgentOS.Application.ModelProfiles;
using AgentOS.Application.Planning;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.ProjectPlanning;

public sealed record ProjectPlanModelResult(
    bool Success,
    string? Content,
    string? ProfileCode,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Calls the configured Planner model with the same fallback behavior as Backlog generation.
/// Planning is intentionally bounded: each call gets one primary and one retry per profile.
/// </summary>
public sealed class ProjectPlanModelRunner(
    IModelProfileRepository modelProfiles,
    IApiCompletionClient completionClient,
    IOptions<PlannerOptions> plannerOptions,
    ILogger<ProjectPlanModelRunner> logger)
{
    public async Task<ProjectPlanModelResult> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var configuration = plannerOptions.Value;
        var profileCodes = new[] { configuration.ProjectPlanModelProfileCode, configuration.DefaultModelProfileCode }
            .Concat(configuration.BacklogFallbackProfileCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? lastCode = null;
        string? lastMessage = null;
        foreach (var profileCode in profileCodes)
        {
            var profile = await modelProfiles.FindByCodeAsync(profileCode, cancellationToken);
            if (profile is null || !profile.IsEnabled
                || !string.Equals(profile.ExecutionMode, "api", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Project planning profile {ProfileCode} is unavailable.", profileCode);
                continue;
            }

            var apiKey = string.IsNullOrWhiteSpace(profile.SecretReference)
                ? Environment.GetEnvironmentVariable(DefaultSecretEnvironment(profile.Provider))
                : Environment.GetEnvironmentVariable(profile.SecretReference);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                lastCode = "planner-secret-unavailable";
                lastMessage = $"No API key is available for profile {profileCode}.";
                continue;
            }

            var settings = ParseSettings(profile);
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var completion = await completionClient.CompleteAsync(
                    new ApiCompletionRequest(
                        settings.BaseUrl,
                        apiKey,
                        profile.Model,
                        systemPrompt,
                        userPrompt,
                        settings.Temperature,
                        Math.Min(maxTokens, settings.MaxTokens ?? maxTokens),
                        Math.Clamp(profile.TimeoutSeconds, 10, 600),
                        profile.Provider),
                    cancellationToken);

                if (completion.Success && !string.IsNullOrWhiteSpace(completion.Content))
                {
                    return new ProjectPlanModelResult(true, completion.Content.Trim(), profile.Code, null, null);
                }

                lastCode = completion.ErrorCode ?? "planner-provider-error";
                lastMessage = completion.ErrorMessage ?? "Planner returned no content.";
                logger.LogWarning(
                    "Project planning profile {ProfileCode} attempt {Attempt} failed: {Code}.",
                    profileCode,
                    attempt,
                    lastCode);
            }
        }

        return new ProjectPlanModelResult(
            false,
            null,
            null,
            lastCode ?? "planner-model-unavailable",
            lastMessage ?? "No Planner model could complete the planning step.");
    }

    private static string DefaultSecretEnvironment(string provider)
        => provider.ToLowerInvariant() switch
        {
            "kimi" or "moonshot" => "KIMI_API_KEY",
            "openai" => "OPENAI_API_KEY",
            "deepseek" => "DEEPSEEK_API_KEY",
            "glm" or "zhipu" => "ZHIPU_API_KEY",
            "qwen" or "dashscope" => "DASHSCOPE_API_KEY",
            "doubao" or "ark" => "ARK_API_KEY",
            "hunyuan" => "HUNYUAN_API_KEY",
            "grok" or "xai" => "XAI_API_KEY",
            _ => "OPENAI_API_KEY"
        };

    private static string DefaultBaseUrl(string provider)
        => provider.ToLowerInvariant() switch
        {
            "kimi" or "moonshot" => "https://api.moonshot.cn/v1",
            "openai" => "https://api.openai.com/v1",
            "deepseek" => "https://api.deepseek.com/v1",
            "glm" or "zhipu" => "https://open.bigmodel.cn/api/paas/v4",
            "qwen" or "dashscope" => "https://dashscope.aliyuncs.com/compatible-mode/v1",
            "doubao" or "ark" => "https://ark.cn-beijing.volces.com/api/v3",
            "hunyuan" => "https://api.hunyuan.cloud.tencent.com/v1",
            "grok" or "xai" => "https://api.x.ai/v1",
            _ => string.Empty
        };

    private static (string BaseUrl, double? Temperature, int? MaxTokens) ParseSettings(
        AgentOS.Domain.ModelProfiles.ModelProfile profile)
    {
        var baseUrl = DefaultBaseUrl(profile.Provider);
        double? temperature = null;
        int? maxTokens = null;
        try
        {
            using var document = JsonDocument.Parse(profile.ConfigurationJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("baseUrl", out var url)
                    && url.ValueKind == JsonValueKind.String)
                {
                    baseUrl = url.GetString() ?? baseUrl;
                }

                if (document.RootElement.TryGetProperty("temperature", out var temp)
                    && temp.ValueKind == JsonValueKind.Number
                    && temp.TryGetDouble(out var parsedTemp))
                {
                    temperature = parsedTemp;
                }

                if (document.RootElement.TryGetProperty("maxTokens", out var tokens)
                    && tokens.ValueKind == JsonValueKind.Number
                    && tokens.TryGetInt32(out var parsedTokens))
                {
                    maxTokens = parsedTokens;
                }
            }
        }
        catch (JsonException)
        {
            // Profile validation guarantees JSON; defaults are safe.
        }

        return (baseUrl, temperature, maxTokens);
    }
}
