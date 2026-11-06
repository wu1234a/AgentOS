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

                return TaskHandlerResult.Succeeded(result.ExitCode ?? 0, result.Summary)
                    with
                    {
                        Artifacts = BuildArtifacts(result)
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
