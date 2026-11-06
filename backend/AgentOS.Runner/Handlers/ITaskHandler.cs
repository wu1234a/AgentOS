using AgentOS.Application.ModelProfiles;

namespace AgentOS.Runner.Handlers;

/// <summary>Everything a handler may use. Handlers never receive credentials or arbitrary paths.</summary>
public sealed record TaskExecutionContext(
    Guid TaskId,
    Guid AttemptId,
    string TaskCode,
    string Title,
    string Priority,
    string ProjectSlug,
    string AgentCode,
    string AgentRole,
    string? SkillCode,
    string? SkillContent,
    string WorkspacePath,
    string InputJson,
    int AttemptNumber,
    int TimeoutSeconds,
    Action<string, string> Log,
    ResolvedModelProfile? ModelProfile = null);

public enum HandlerOutcome
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2
}

/// <summary>Local file a handler wants uploaded as a task artifact.</summary>
public sealed record TaskArtifactFile(string Name, string MediaType, string Path);

/// <summary>Standardized handler result; the runner maps it to complete/fail.</summary>
public sealed record TaskHandlerResult(
    HandlerOutcome Outcome,
    int ExitCode = 0,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool Retryable = false,
    string? Summary = null,
    IReadOnlyList<TaskArtifactFile>? Artifacts = null)
{
    public static TaskHandlerResult Succeeded(int exitCode = 0, string? summary = null)
        => new(HandlerOutcome.Succeeded, exitCode, Summary: summary);

    public static TaskHandlerResult Failed(string errorCode, string errorMessage, bool retryable = true)
        => new(HandlerOutcome.Failed, 1, errorCode, errorMessage, retryable);

    public static TaskHandlerResult Cancelled() => new(HandlerOutcome.Cancelled, 130, "cancelled", "Cancelled");
}

/// <summary>Controlled task execution abstraction. Codex CLI is one implementation.</summary>
public interface ITaskHandler
{
    string Name { get; }

    Task<TaskHandlerResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken);
}
