using System.Text.Json;
using AgentOS.AgentAdapters;
using AgentOS.AgentAdapters.Codex;
using AgentOS.Runner.Handlers;
using Microsoft.Extensions.Options;

namespace AgentOS.Runner;

/// <summary>
/// Runs a task through the configured agent adapter (Codex CLI) inside the validated workspace.
/// Mapping is conservative: only an explicit success from the adapter becomes Succeeded.
/// </summary>
public sealed class CodexTaskHandler(
    IAgentAdapter adapter,
    IValidationCommandRunner validationRunner,
    IChangeSetCapture changeSetCapture,
    IOptions<CodexCliOptions> codexOptions,
    ILogger<CodexTaskHandler> logger) : ITaskHandler
{
    public string Name => adapter.Code;

    public async Task<TaskHandlerResult> ExecuteAsync(
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        var options = codexOptions.Value;

        if (!CodexCliOptions.TryParseSandbox(options.SandboxMode, out var sandbox))
        {
            logger.LogError("Codex sandbox mode '{Mode}' is not supported.", options.SandboxMode);
            return TaskHandlerResult.Failed(
                "codex-sandbox-invalid",
                $"Sandbox mode '{options.SandboxMode}' is not supported; use read-only or workspace-write.",
                retryable: false);
        }

        var profile = context.ModelProfile;

        if (profile is not null)
        {
            if (!string.Equals(profile.Provider, "codex-cli", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(profile.ExecutionMode, "cli", StringComparison.OrdinalIgnoreCase))
            {
                context.Log(
                    "error",
                    $"model profile {profile.Code} requires provider={profile.Provider} mode={profile.ExecutionMode}");
                return TaskHandlerResult.Failed(
                    "model-provider-unsupported",
                    $"Provider '{profile.Provider}' with mode '{profile.ExecutionMode}' is not supported by the Codex CLI adapter.",
                    retryable: false);
            }

            context.Log(
                "info",
                $"using model profile {profile.Code} ({profile.Provider}/{profile.Model}) from {profile.Source}");
        }

        var prompt = PromptBuilder.Build(new AgentTaskPromptContext(
            context.TaskCode,
            context.Title,
            context.Priority,
            context.AttemptNumber,
            context.ProjectSlug,
            context.AgentCode,
            context.AgentRole,
            context.InputJson,
            context.SkillCode,
            context.SkillContent));

        context.Log("info", $"starting {adapter.Code} for {context.TaskCode} in {context.WorkspacePath}");

        var policy = new AgentExecutionPolicy(
            sandbox,
            options.EnvironmentAllowList,
            options.MaxOutputBytes);

        var request = new AgentExecutionRequest(
            context.TaskId,
            context.AttemptId,
            context.ProjectSlug,
            context.WorkspacePath,
            prompt,
            Math.Min(
                context.TimeoutSeconds,
                Math.Min(profile?.TimeoutSeconds ?? int.MaxValue, options.TimeoutSeconds)),
            policy,
            context.Log,
            profile?.Model);

        var result = await adapter.ExecuteAsync(request, cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.ExternalSessionId))
        {
            context.Log("info", $"codex session {result.ExternalSessionId}");
        }

        switch (result.Status)
        {
            case AgentExecutionStatus.Succeeded:
                if (!string.IsNullOrWhiteSpace(result.Summary))
                {
                    context.Log("info", $"codex summary: {result.Summary}");
                }

                var validationFailure = await ValidateAsync(context, result, cancellationToken);
                if (validationFailure is not null)
                {
                    return validationFailure;
                }

                var artifacts = BuildArtifacts(result);
                var baseRevision = ReadBaseRevision(context.InputJson);
                if (!string.IsNullOrWhiteSpace(baseRevision))
                {
                    var capture = await changeSetCapture.CaptureAsync(
                        context.WorkspacePath,
                        baseRevision,
                        cancellationToken);

                    if (!capture.Success || capture.Artifact is null)
                    {
                        context.Log(
                            "error",
                            $"change set capture failed: {capture.ErrorCode} {capture.ErrorMessage}");
                        return TaskHandlerResult.Failed(
                            capture.ErrorCode ?? "changeset-error",
                            capture.ErrorMessage ?? "Could not capture the change set.",
                            retryable: false) with
                        {
                            Artifacts = artifacts
                        };
                    }

                    artifacts = Append(
                        artifacts,
                        new TaskArtifactFile("changeset.diff", "text/plain", capture.Artifact.DiffPath),
                        new TaskArtifactFile("changeset.json", "application/json", capture.Artifact.MetadataPath));

                    context.Log(
                        "info",
                        $"change set captured: {capture.Artifact.Files.Count} file(s), base {baseRevision} head {capture.Artifact.HeadRevision}");
                }

                return TaskHandlerResult.Succeeded(result.ExitCode ?? 0, result.Summary)
                    with
                    {
                        Artifacts = artifacts
                    };

            case AgentExecutionStatus.Cancelled:
                context.Log("warn", "codex execution was cancelled");
                return TaskHandlerResult.Cancelled() with { Artifacts = BuildArtifacts(result) };

            case AgentExecutionStatus.TimedOut:
                context.Log("error", result.ErrorMessage ?? "codex timed out");
                return TaskHandlerResult.Failed(
                    result.ErrorCode ?? "codex-timeout",
                    result.ErrorMessage ?? "Codex timed out.",
                    retryable: true) with { Artifacts = BuildArtifacts(result) };

            case AgentExecutionStatus.Unavailable:
                context.Log("error", result.ErrorMessage ?? "codex is unavailable");
                return TaskHandlerResult.Failed(
                    result.ErrorCode ?? "codex-unavailable",
                    result.ErrorMessage ?? "Codex CLI is unavailable.",
                    retryable: false);

            default:
                context.Log(
                    "error",
                    result.ErrorMessage ?? $"codex failed with exit code {result.ExitCode}");
                return TaskHandlerResult.Failed(
                    result.ErrorCode ?? $"codex-exit-{result.ExitCode}",
                    result.ErrorMessage ?? "Codex failed.",
                    retryable: true) with { Artifacts = BuildArtifacts(result) };
        }
    }

    private async Task<TaskHandlerResult?> ValidateAsync(
        TaskExecutionContext context,
        AgentExecutionResult result,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ValidationCommand> commands;
        try
        {
            commands = ParseValidationCommands(context.InputJson);
        }
        catch (JsonException)
        {
            return TaskHandlerResult.Failed(
                "validation-config-invalid",
                "Coding snapshot validationCommands is not valid JSON.",
                retryable: false);
        }
        catch (FormatException exception)
        {
            return TaskHandlerResult.Failed(
                "validation-config-invalid",
                exception.Message,
                retryable: false);
        }

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            context.Log("info", $"validation {index + 1}/{commands.Count}: {command.Command}");

            var validation = await validationRunner.RunAsync(
                context.WorkspacePath,
                command,
                context.Log,
                cancellationToken);

            if (validation.Success)
            {
                continue;
            }

            context.Log(
                "error",
                $"validation failed: {validation.ErrorCode} (exit {validation.ExitCode?.ToString() ?? "n/a"})");

            return TaskHandlerResult.Failed(
                validation.ErrorCode ?? "validation-failed",
                validation.ErrorMessage ?? "Validation command failed.",
                retryable: validation.ErrorCode == "validation-error") with
            {
                Artifacts = BuildArtifacts(result)
            };
        }

        if (commands.Count > 0)
        {
            context.Log("info", $"validation passed ({commands.Count} command(s))");
        }

        return null;
    }

    internal static IReadOnlyList<ValidationCommand> ParseValidationCommands(string inputJson)
    {
        using var document = JsonDocument.Parse(inputJson);

        if (!document.RootElement.TryGetProperty("validationCommands", out var commands)
            || commands.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (commands.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("validationCommands must be an array.");
        }

        if (commands.GetArrayLength() > 10)
        {
            throw new FormatException("At most 10 validation commands are allowed.");
        }

        var parsed = new List<ValidationCommand>();
        foreach (var item in commands.EnumerateArray())
        {
            string? command = null;
            var timeout = 600;

            if (item.ValueKind == JsonValueKind.String)
            {
                command = item.GetString();
            }
            else if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("command", out var commandValue)
                && commandValue.ValueKind == JsonValueKind.String)
            {
                command = commandValue.GetString();
                if (item.TryGetProperty("timeoutSeconds", out var timeoutValue)
                    && timeoutValue.ValueKind == JsonValueKind.Number
                    && timeoutValue.TryGetInt32(out var parsedTimeout))
                {
                    timeout = parsedTimeout;
                }
            }

            if (string.IsNullOrWhiteSpace(command) || command.Length > 1000)
            {
                throw new FormatException("Each validation command must be a non-empty string up to 1000 characters.");
            }

            if (timeout is < 1 or > 1800)
            {
                throw new FormatException("Validation timeoutSeconds must be between 1 and 1800.");
            }

            parsed.Add(new ValidationCommand(command, timeout));
        }

        return parsed;
    }

    private static IReadOnlyList<TaskArtifactFile>? Append(
        IReadOnlyList<TaskArtifactFile>? existing,
        params TaskArtifactFile[] additional)
    {
        var combined = new List<TaskArtifactFile>();
        if (existing is not null)
        {
            combined.AddRange(existing);
        }

        combined.AddRange(additional);
        return combined;
    }

    private static string? ReadBaseRevision(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            return document.RootElement.TryGetProperty("baseRevision", out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<TaskArtifactFile>? BuildArtifacts(AgentExecutionResult result)
    {
        var artifacts = new List<TaskArtifactFile>();

        if (!string.IsNullOrWhiteSpace(result.StdoutArtifactPath))
        {
            artifacts.Add(new TaskArtifactFile("stdout.log", "text/plain", result.StdoutArtifactPath));
        }

        if (!string.IsNullOrWhiteSpace(result.StderrArtifactPath))
        {
            artifacts.Add(new TaskArtifactFile("stderr.log", "text/plain", result.StderrArtifactPath));
        }

        return artifacts.Count == 0 ? null : artifacts;
    }
}
