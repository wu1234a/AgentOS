using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentOS.AgentAdapters;

/// <summary>
/// Generic OpenAI-compatible chat/tool-loop adapter. It lets any OpenAI-compatible provider
/// (Kimi, DeepSeek, GLM, Qwen, Doubao, Hunyuan, Grok, OpenAI, LiteLLM) edit files and run
/// commands inside the validated workspace through a bounded tool loop.
/// </summary>
public sealed class OpenAiCompatibleAgentAdapter : IAgentAdapter
{
    private const int MaxSteps = 24;
    private const int MaxFileBytes = 200_000;
    private const int MaxCommandOutput = 100_000;
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleAgentAdapter(HttpClient? httpClient = null)
        => _httpClient = httpClient ?? new HttpClient();

    public string Code => "openai-compatible";

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var provider = ResolveProvider(request.Provider);
        if (provider is null)
        {
            return Unavailable(started, "model-provider-unsupported",
                $"Provider '{request.Provider}' is not configured for OpenAI-compatible execution.");
        }

        if (!string.Equals(provider.Kind, "openai-compatible", StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable(started, "model-provider-unsupported",
                $"Provider '{provider.Code}' uses kind '{provider.Kind}', which this adapter cannot execute.");
        }

        var settings = ReadSettings(request.ConfigurationJson);
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? provider.BaseUrl : settings.BaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return Unavailable(started, "model-base-url-invalid", "A valid https provider base URL is required.");
        }

        var apiKey = ResolveSecret(request.SecretReference, settings.ApiKeyEnvironment, provider.SecretEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unavailable(started, "model-secret-unavailable",
                $"Set {provider.SecretEnvironmentVariable} (or SecretReference) for provider {provider.Code}.");
        }

        var model = request.Model ?? settings.Model ?? provider.DefaultModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            return Unavailable(started, "model-not-configured", "Model id is required.");
        }

        var workspace = Path.GetFullPath(request.WorkingDirectory);
        var messages = new List<object>
        {
            new { role = "system", content = BuildSystemPrompt(request.Policy.SandboxMode, workspace) },
            new { role = "user", content = request.Prompt }
        };
        var tools = BuildTools(request.Policy.SandboxMode);
        var endpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/chat/completions");

        for (var step = 0; step < MaxSteps; step++)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 10, 1800)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            object payload = new
            {
                model,
                messages,
                tools,
                tool_choice = "auto",
                temperature = settings.Temperature,
                max_tokens = settings.MaxTokens
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(httpRequest, linked.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return Failed(started, "model-timeout", "Model request timed out.", retryable: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(started);
            }
            catch (Exception exception)
            {
                return Failed(started, "model-request-failed", Sanitize(exception.Message, apiKey), retryable: true);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(linked.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return Failed(started, "model-http-error",
                        Sanitize($"HTTP {(int)response.StatusCode}: {body}", apiKey), retryable: true);
                }

                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                {
                    return Failed(started, "model-invalid-response", "Provider returned no choices.", retryable: false);
                }

                var message = choices[0].GetProperty("message");
                var content = message.TryGetProperty("content", out var contentElement)
                    && contentElement.ValueKind == JsonValueKind.String
                    ? contentElement.GetString()
                    : null;

                if (message.TryGetProperty("tool_calls", out var toolCalls)
                    && toolCalls.ValueKind == JsonValueKind.Array
                    && toolCalls.GetArrayLength() > 0)
                {
                    messages.Add(message.Clone());
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        var id = toolCall.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N");
                        var function = toolCall.GetProperty("function");
                        var name = function.GetProperty("name").GetString() ?? string.Empty;
                        var arguments = function.TryGetProperty("arguments", out var argsElement)
                            ? argsElement.GetString() ?? "{}"
                            : "{}";
                        var result = await ExecuteToolAsync(name, arguments, workspace, request.Policy, cancellationToken);
                        messages.Add(new { role = "tool", tool_call_id = id, content = result });
                        request.OnLog?.Invoke("info", $"tool {name}: {result[..Math.Min(200, result.Length)]}");
                    }

                    continue;
                }

                return new AgentExecutionResult(
                    AgentExecutionStatus.Succeeded,
                    null,
                    started,
                    DateTime.UtcNow,
                    null,
                    null,
                    content,
                    null,
                    null,
                    null);
            }
        }

        return Failed(started, "model-step-limit", $"Model exceeded {MaxSteps} tool steps.", retryable: true);
    }

    private static ModelProviderPreset? ResolveProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return ModelProviderPresets.All["openai-compatible"];
        }

        return ModelProviderPresets.All.TryGetValue(provider, out var preset)
            ? preset
            : new ModelProviderPreset(provider, "openai-compatible", string.Empty, null, "OPENAI_API_KEY");
    }

    private static ProviderSettings ReadSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProviderSettings();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new ProviderSettings
            {
                BaseUrl = root.TryGetProperty("baseUrl", out var baseUrl) ? baseUrl.GetString() : null,
                ApiKeyEnvironment = root.TryGetProperty("apiKeyEnv", out var key) ? key.GetString() : null,
                Model = root.TryGetProperty("model", out var model) ? model.GetString() : null,
                Temperature = root.TryGetProperty("temperature", out var temperature) && temperature.TryGetDouble(out var t) ? t : 0.1,
                MaxTokens = root.TryGetProperty("maxTokens", out var maxTokens) && maxTokens.TryGetInt32(out var m) ? m : 8192
            };
        }
        catch (JsonException)
        {
            return new ProviderSettings();
        }
    }

    private static string? ResolveSecret(string? secretReference, string? configEnvironment, string presetEnvironment)
    {
        foreach (var name in new[] { secretReference, configEnvironment, presetEnvironment })
        {
            if (!string.IsNullOrWhiteSpace(name)
                && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            {
                return Environment.GetEnvironmentVariable(name);
            }
        }

        return null;
    }

    private static string BuildSystemPrompt(AgentSandboxMode sandbox, string workspace)
        => $"""
           You are AgentOS, an autonomous coding agent working in this workspace: {workspace}
           Sandbox: {(sandbox == AgentSandboxMode.ReadOnly ? "read-only (do not write files or run mutating commands)" : "workspace-write")}.
           Use the provided tools to inspect the repository, make focused changes, and run relevant checks.
           Never read or print secrets or files outside the workspace. Keep changes minimal and explain the final result.
           When the task is complete, answer with a concise summary of files changed and validation performed.
           """;

    private static object[] BuildTools(AgentSandboxMode sandbox)
    {
        var tools = new List<object>
        {
            Tool("list_files", "List files under a workspace-relative directory.", new
            {
                type = "object",
                properties = new { path = new { type = "string", description = "Relative directory, default ." } }
            }),
            Tool("read_file", "Read a UTF-8 text file inside the workspace.", new
            {
                type = "object",
                properties = new { path = new { type = "string" } },
                required = new[] { "path" }
            })
        };

        if (sandbox == AgentSandboxMode.WorkspaceWrite)
        {
            tools.Add(Tool("write_file", "Create or replace a UTF-8 text file inside the workspace.", new
            {
                type = "object",
                properties = new { path = new { type = "string" }, content = new { type = "string" } },
                required = new[] { "path", "content" }
            }));
            tools.Add(Tool("run_command", "Run a shell command in the workspace and return stdout/stderr.", new
            {
                type = "object",
                properties = new { command = new { type = "string" } },
                required = new[] { "command" }
            }));
        }

        return tools.ToArray();
    }

    private static object Tool(string name, string description, object parameters)
        => new { type = "function", function = new { name, description, parameters } };

    private static async Task<string> ExecuteToolAsync(
        string name,
        string arguments,
        string workspace,
        AgentExecutionPolicy policy,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments);
            var root = document.RootElement;
            return name switch
            {
                "list_files" => ListFiles(workspace, root.TryGetProperty("path", out var listPath) ? listPath.GetString() : "."),
                "read_file" => ReadFile(workspace, Required(root, "path")),
                "write_file" when policy.SandboxMode == AgentSandboxMode.WorkspaceWrite
                    => WriteFile(workspace, Required(root, "path"), root.TryGetProperty("content", out var content) ? content.GetString() ?? string.Empty : string.Empty),
                "run_command" when policy.SandboxMode == AgentSandboxMode.WorkspaceWrite
                    => await RunCommandAsync(workspace, Required(root, "command"), cancellationToken),
                _ => $"error: tool '{name}' is not available in {policy.SandboxMode} mode"
            };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return $"error: {exception.Message}";
        }
    }

    private static string ListFiles(string workspace, string? relative)
    {
        var directory = ResolvePath(workspace, relative ?? ".");
        if (!Directory.Exists(directory))
        {
            return "error: directory not found";
        }

        return string.Join('\n', Directory.EnumerateFileSystemEntries(directory)
            .Take(200)
            .Select(path => Path.GetRelativePath(workspace, path) + (Directory.Exists(path) ? "/" : string.Empty)));
    }

    private static string ReadFile(string workspace, string relative)
    {
        var path = ResolvePath(workspace, relative);
        if (!File.Exists(path))
        {
            return "error: file not found";
        }

        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
        {
            return $"error: file too large ({info.Length} bytes)";
        }

        return File.ReadAllText(path);
    }

    private static string WriteFile(string workspace, string relative, string content)
    {
        var path = ResolvePath(workspace, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return $"wrote {content.Length} characters to {Path.GetRelativePath(workspace, path)}";
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
        var outputTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var errorTask = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            var output = await outputTask;
            var error = await errorTask;
            var combined = (output + "\n" + error).Trim();
            return combined.Length <= MaxCommandOutput ? combined : combined[..MaxCommandOutput];
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            return "error: command timed out or was cancelled";
        }
    }

    private static string Required(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new InvalidOperationException($"missing {name}")
            : throw new InvalidOperationException($"missing {name}");

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

    private static string Sanitize(string text, string secret)
        => string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "[redacted]", StringComparison.Ordinal);

    private static AgentExecutionResult Unavailable(DateTime started, string code, string message)
        => new(AgentExecutionStatus.Unavailable, null, started, DateTime.UtcNow, null, null, null, code, message, null);

    private static AgentExecutionResult Failed(DateTime started, string code, string message, bool retryable)
        => new(AgentExecutionStatus.Failed, 1, started, DateTime.UtcNow, null, null, null, code, message, null);

    private static AgentExecutionResult Cancelled(DateTime started)
        => new(AgentExecutionStatus.Cancelled, 130, started, DateTime.UtcNow, null, null, null, "cancelled", "Cancelled", null);

    private sealed class ProviderSettings
    {
        public string? BaseUrl { get; init; }
        public string? ApiKeyEnvironment { get; init; }
        public string? Model { get; init; }
        public double Temperature { get; init; } = 0.1;
        public int MaxTokens { get; init; } = 8192;
    }
}
