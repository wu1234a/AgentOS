using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentOS.Application.Planning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOS.Infrastructure.Planning;

/// <summary>
/// OpenAI-compatible chat completion client. Only HTTPS hosts listed in Planner:AllowedProviderHosts
/// are contacted; the API key is supplied per request and never logged.
/// </summary>
public sealed class ApiCompletionClient(
    IHttpClientFactory httpClientFactory,
    IOptions<PlannerOptions> options,
    ILogger<ApiCompletionClient> logger) : IApiCompletionClient
{
    public async Task<ApiCompletionResult> CompleteAsync(
        ApiCompletionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return Failure("planner-invalid-base-url", "Provider base URL must be an absolute https URL.");
        }

        var allowed = options.Value.AllowedProviderHosts
            .Any(host => string.Equals(host, baseUri.Host, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            return Failure(
                "planner-host-not-allowed",
                $"Provider host '{baseUri.Host}' is not in Planner:AllowedProviderHosts.");
        }

        var endpoint = new Uri(
            baseUri.ToString().TrimEnd('/') + "/chat/completions");

        var payload = new Dictionary<string, object>
        {
            ["model"] = request.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            }
        };

        // Omit unset optional fields; some OpenAI-compatible providers reject nulls.
        if (request.Temperature is { } temperature)
        {
            payload["temperature"] = temperature;
        }

        if (request.MaxTokens is { } maxTokens)
        {
            // Kimi/OpenAI-compatible APIs use max_completion_tokens for newer models.
            payload["max_completion_tokens"] = maxTokens;
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = httpClientFactory.CreateClient("planner");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var response = await client.SendAsync(httpRequest, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(linked.Token);
                var snippet = Sanitize(errorBody, request.ApiKey);
                logger.LogWarning(
                    "Planner provider {Provider} returned HTTP {Status}: {Detail}",
                    request.Provider,
                    (int)response.StatusCode,
                    snippet);
                return Failure(
                    "planner-provider-http",
                    $"Provider returned HTTP {(int)response.StatusCode}: {snippet}");
            }

            var body = await response.Content.ReadAsStringAsync(linked.Token);
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return Failure("planner-invalid-response", "Provider response did not contain message content.");
            }

            var firstChoice = choices[0];
            if (firstChoice.ValueKind != JsonValueKind.Object
                || !firstChoice.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                return Failure("planner-invalid-response", "Provider response did not contain message content.");
            }

            int? promptTokens = null;
            int? completionTokens = null;
            if (document.RootElement.TryGetProperty("usage", out var usage)
                && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var prompt)
                    && prompt.TryGetInt32(out var promptValue))
                {
                    promptTokens = promptValue;
                }

                if (usage.TryGetProperty("completion_tokens", out var completion)
                    && completion.TryGetInt32(out var completionValue))
                {
                    completionTokens = completionValue;
                }
            }

            return new ApiCompletionResult(
                true,
                content.GetString(),
                null,
                null,
                200,
                promptTokens,
                completionTokens);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Failure("planner-timeout", "Planner provider request timed out.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure("planner-cancelled", "Planner request was cancelled.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Planner provider request failed.");
            return Failure("planner-network-error", "Provider request failed.");
        }
        catch (JsonException)
        {
            return Failure("planner-invalid-response", "Provider response was not valid JSON.");
        }
    }

    private static string Sanitize(string body, string apiKey)
    {
        var text = body.Length > 500 ? body[..500] : body;

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            text = text.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal);
        }

        text = Regex.Replace(text, "sk-[A-Za-z0-9_\\-]{8,}", "[REDACTED]");
        return text.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static ApiCompletionResult Failure(string code, string message)
        => new(false, null, code, message, null);
}
