using System.Net.Http.Json;
using System.Text.Json;
using AgentOS.Application.Tasks;
using Microsoft.Extensions.Options;

namespace AgentOS.Runner;

/// <summary>HTTP implementation of the runner API. Only outbound requests are made.</summary>
public sealed class RunnerApiClient(
    HttpClient httpClient,
    IOptions<RunnerOptions> options) : IRunnerApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private RunnerOptions Options => options.Value;

    public async Task<RunnerRegistrationResponse> RegisterAsync(
        string registrationKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/runners/register")
        {
            Content = JsonContent.Create(
                new
                {
                    workerCode = Options.WorkerCode,
                    displayName = Options.DisplayName,
                    hostName = Options.HostName,
                    platform = Options.Platform,
                    architecture = Options.Architecture,
                    version = Options.Version,
                    capabilitiesJson = "{}"
                },
                options: Json)
        };
        request.Headers.Add("X-AgentOS-Registration-Key", registrationKey);

        return await SendAsync<RunnerRegistrationResponse>(request, cancellationToken)
               ?? throw new RunnerApiException(0, "empty-response", "Registration returned no body.");
    }

    public async Task HeartbeatWorkerAsync(CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/v1/runners/heartbeat");
        await SendAsync<JsonElement>(request, cancellationToken);
    }

    public async Task<IReadOnlyList<ClaimedTask>> ClaimAsync(int capacity, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/v1/runners/claim");
        request.Content = JsonContent.Create(new { capacity }, options: Json);

        var response = await SendAsync<ClaimResponse>(request, cancellationToken);
        return response?.Tasks ?? [];
    }

    public Task<RunnerTaskResponse> StartAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        string? externalSessionId,
        CancellationToken cancellationToken)
        => SendTaskActionAsync(
            $"api/v1/runners/tasks/{taskId}/start",
            new { attemptId, leaseToken, externalSessionId },
            cancellationToken);

    public Task<RunnerTaskResponse> HeartbeatTaskAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        CancellationToken cancellationToken)
        => SendTaskActionAsync(
            $"api/v1/runners/tasks/{taskId}/heartbeat",
            new { attemptId, leaseToken },
            cancellationToken);

    public Task<RunnerTaskResponse> CompleteAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        int exitCode,
        CancellationToken cancellationToken)
        => SendTaskActionAsync(
            $"api/v1/runners/tasks/{taskId}/complete",
            new { attemptId, leaseToken, exitCode },
            cancellationToken);

    public Task<RunnerTaskResponse> FailAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        string errorCode,
        string errorMessage,
        bool retryable,
        bool cancelled,
        CancellationToken cancellationToken)
        => SendTaskActionAsync(
            $"api/v1/runners/tasks/{taskId}/fail",
            new { attemptId, leaseToken, errorCode, errorMessage, retryable, cancelled },
            cancellationToken);

    public async Task UploadArtifactAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        string name,
        string mediaType,
        string filePath,
        CancellationToken cancellationToken)
    {
        var query = string.Join(
            "&",
            $"attemptId={attemptId}",
            $"name={Uri.EscapeDataString(name)}",
            $"mediaType={Uri.EscapeDataString(mediaType)}");
        using var request = CreateRequest(
            HttpMethod.Post,
            $"api/v1/runners/tasks/{taskId}/artifacts?{query}");
        request.Headers.Add("X-AgentOS-Lease-Token", leaseToken);

        await using var stream = File.OpenRead(filePath);
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);

        await SendAsync<JsonElement>(request, cancellationToken);
    }

    public async Task AppendLogsAsync(
        Guid taskId,
        Guid attemptId,
        string leaseToken,
        IReadOnlyList<TaskLogEntry> entries,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"api/v1/runners/tasks/{taskId}/logs");
        request.Content = JsonContent.Create(
            new
            {
                attemptId,
                leaseToken,
                entries = entries.Select(entry => new
                {
                    sequence = entry.Sequence,
                    level = entry.Level,
                    message = entry.Message,
                    timestampUtc = entry.TimestampUtc
                })
            },
            options: Json);

        await SendAsync<JsonElement>(request, cancellationToken);
    }

    private Task<RunnerTaskResponse> SendTaskActionAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        var request = CreateRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(body, options: Json);
        return SendAsync<RunnerTaskResponse>(request, cancellationToken)!;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);

        if (!string.IsNullOrWhiteSpace(Options.ClientId) && !string.IsNullOrWhiteSpace(Options.ClientSecret))
        {
            request.Headers.Add("X-AgentOS-Client-Id", Options.ClientId);
            request.Headers.Add("X-AgentOS-Client-Secret", Options.ClientSecret);
        }
        else if (!string.IsNullOrWhiteSpace(Options.DevApiKey))
        {
            request.Headers.Add("X-AgentOS-Dev-Key", Options.DevApiKey);
            request.Headers.Add("X-AgentOS-Worker-Code", Options.WorkerCode);
        }

        return request;
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new RunnerApiException((int)response.StatusCode, ExtractCode(body), body);
            }

            return string.IsNullOrWhiteSpace(body)
                ? default
                : JsonSerializer.Deserialize<T>(body, Json);
        }
        catch (HttpRequestException exception)
        {
            throw new RunnerApiException(0, "network-error", exception.Message);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RunnerApiException(408, "timeout", exception.Message);
        }
    }

    private static string ExtractCode(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "unknown";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("code", out var code)
                ? code.GetString() ?? "unknown"
                : "unknown";
        }
        catch (JsonException)
        {
            return "unknown";
        }
    }
}
