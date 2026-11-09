using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentOS.AgentAdapters;

namespace AgentOS.AgentAdapters;

/// <summary>Anthropic Messages API adapter for Claude/Opus models.</summary>
public sealed class AnthropicAgentAdapter : IAgentAdapter
{
    private const int MaxSteps = 24;
    private readonly HttpClient _httpClient;

    public AnthropicAgentAdapter(HttpClient? httpClient = null)
        => _httpClient = httpClient ?? new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    public string Code => "anthropic";

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var settings = ReadSettings(request.ConfigurationJson);
        var model = request.Model ?? settings.Model ?? "claude-sonnet-4-20250514";
        var apiKey = ResolveSecret(request.SecretReference, settings.ApiKeyEnvironment);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unavailable(started, "model-secret-unavailable", "Set ANTHROPIC_API_KEY or SecretReference.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? "https://api.anthropic.com" : settings.BaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return Unavailable(started, "model-base-url-invalid", "A valid https Anthropic base URL is required.");
        }

        var workspace = Path.GetFullPath(request.WorkingDirectory);
        var messages = new List<object> { new { role = "user", content = request.Prompt } };
        var tools = BuildTools(request.Policy.SandboxMode);

        for (var step = 0; step < MaxSteps; step++)
        {
            object payload = new
            {
                model,
                max_tokens = settings.MaxTokens,
                system = BuildSystemPrompt(request.Policy.SandboxMode, workspace),
                messages,
                tools
            };
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/v1/messages"))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("x-api-key", apiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 10, 1800)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(httpRequest, linked.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return Failed(started, "model-timeout", "Anthropic request timed out.", true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(started);
            }
            catch (Exception exception)
            {
                return Failed(started, "model-request-failed", Sanitize(exception.Message, apiKey), true);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(linked.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return Failed(started, "model-http-error", Sanitize($"HTTP {(int)response.StatusCode}: {body}", apiKey), true);
                }

                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                {
                    return Failed(started, "model-invalid-response", "Anthropic response had no content.", false);
                }

                var text = new StringBuilder();
                var toolUses = new List<JsonElement>();
                foreach (var block in content.EnumerateArray())
                {
                    var type = block.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                    if (type == "text" && block.TryGetProperty("text", out var textElement))
                    {
                        text.Append(textElement.GetString());
                    }
                    else if (type == "tool_use")
                    {
                        toolUses.Add(block.Clone());
                    }
                }

                if (toolUses.Count == 0)
                {
                    return new AgentExecutionResult(AgentExecutionStatus.Succeeded, null, started, DateTime.UtcNow,
                        null, null, text.ToString(), null, null, null);
                }

                messages.Add(new { role = "assistant", content = content.Clone() });
                var results = new List<object>();
                foreach (var toolUse in toolUses)
                {
                    var name = toolUse.GetProperty("name").GetString() ?? string.Empty;
                    var id = toolUse.GetProperty("id").GetString() ?? string.Empty;
                    var input = toolUse.TryGetProperty("input", out var inputElement) ? inputElement : default;
                    var result = await ExecuteToolAsync(name, input, workspace, request.Policy, cancellationToken);
                    results.Add(new { type = "tool_result", tool_use_id = id, content = result });
                }

                messages.Add(new { role = "user", content = results });
            }
        }

        return Failed(started, "model-step-limit", $"Anthropic exceeded {MaxSteps} tool steps.", true);
    }

    private static object[] BuildTools(AgentSandboxMode sandbox)
    {
        var tools = new List<object>
        {
            new { name = "list_files", description = "List files in a workspace-relative directory.", input_schema = new { type = "object", properties = new { path = new { type = "string" } } } },
            new { name = "read_file", description = "Read a workspace text file.", input_schema = new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" } } }
        };
        if (sandbox == AgentSandboxMode.WorkspaceWrite)
        {
            tools.Add(new { name = "write_file", description = "Write a workspace text file.", input_schema = new { type = "object", properties = new { path = new { type = "string" }, content = new { type = "string" } }, required = new[] { "path", "content" } } });
            tools.Add(new { name = "run_command", description = "Run a shell command in the workspace.", input_schema = new { type = "object", properties = new { command = new { type = "string" } }, required = new[] { "command" } } });
        }

        return tools.ToArray();
    }

    private static async Task<string> ExecuteToolAsync(string name, JsonElement input, string workspace, AgentExecutionPolicy policy, CancellationToken cancellationToken)
    {
        try
        {
            return name switch
            {
                "list_files" => ListFiles(workspace, input.TryGetProperty("path", out var path) ? path.GetString() : "."),
                "read_file" => ReadFile(workspace, String(input, "path")),
                "write_file" when policy.SandboxMode == AgentSandboxMode.WorkspaceWrite
                    => WriteFile(workspace, String(input, "path"), String(input, "content")),
                "run_command" when policy.SandboxMode == AgentSandboxMode.WorkspaceWrite
                    => await RunCommandAsync(workspace, String(input, "command"), cancellationToken),
                _ => $"error: tool '{name}' is not available in {policy.SandboxMode} mode"
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return $"error: {exception.Message}";
        }
    }

    private static string ListFiles(string workspace, string? relative)
    {
        var path = ResolvePath(workspace, relative ?? ".");
        return Directory.Exists(path)
            ? string.Join('\n', Directory.EnumerateFileSystemEntries(path).Take(200).Select(entry => Path.GetRelativePath(workspace, entry)))
            : "error: directory not found";
    }

    private static string ReadFile(string workspace, string relative)
    {
        var path = ResolvePath(workspace, relative);
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "error: file not found";
    }

    private static string WriteFile(string workspace, string relative, string content)
    {
        var path = ResolvePath(workspace, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return $"wrote {content.Length} characters";
    }

    private static async Task<string> RunCommandAsync(string workspace, string command, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
                WorkingDirectory = workspace,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(linked.Token);
            var error = await process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            return (output + "\n" + error).Trim();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (Exception) { }
            return "error: command timed out";
        }
    }

    private static string ResolvePath(string workspace, string relative)
    {
        var root = Path.GetFullPath(workspace);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("path escapes the workspace");
        }

        return path;
    }

    private static string String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string BuildSystemPrompt(AgentSandboxMode sandbox, string workspace)
        => $"You are an autonomous coding agent in {workspace}. Sandbox: {sandbox}. Use tools, keep changes focused, never access secrets or paths outside the workspace.";

    private static string? ResolveSecret(string? secretReference, string? configEnvironment)
    {
        foreach (var name in new[] { secretReference, configEnvironment, "ANTHROPIC_API_KEY" })
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            {
                return Environment.GetEnvironmentVariable(name);
            }
        }

        return null;
    }

    private static ProviderSettings ReadSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ProviderSettings();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new ProviderSettings
            {
                BaseUrl = root.TryGetProperty("baseUrl", out var baseUrl) ? baseUrl.GetString() : null,
                Model = root.TryGetProperty("model", out var model) ? model.GetString() : null,
                ApiKeyEnvironment = root.TryGetProperty("apiKeyEnv", out var key) ? key.GetString() : null,
                MaxTokens = root.TryGetProperty("maxTokens", out var maxTokens) && maxTokens.TryGetInt32(out var value) ? value : 8192
            };
        }
        catch (JsonException) { return new ProviderSettings(); }
    }

    private static string Sanitize(string text, string secret)
        => string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "[redacted]", StringComparison.Ordinal);

    private static AgentExecutionResult Unavailable(DateTime started, string code, string message)
        => new(AgentExecutionStatus.Unavailable, null, started, DateTime.UtcNow, null, null, null, code, message, null);

    private static AgentExecutionResult Failed(DateTime started, string code, string message, bool retryable)
        => new(AgentExecutionStatus.Failed, retryable ? 1 : 2, started, DateTime.UtcNow, null, null, null, code, message, null);

    private static AgentExecutionResult Cancelled(DateTime started)
        => new(AgentExecutionStatus.Cancelled, 130, started, DateTime.UtcNow, null, null, null, "cancelled", "Cancelled", null);

    private sealed class ProviderSettings
    {
        public string? BaseUrl { get; init; }
        public string? ApiKeyEnvironment { get; init; }
        public string? Model { get; init; }
        public int MaxTokens { get; init; } = 8192;
    }
}
