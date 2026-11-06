namespace AgentOS.Runner.Handlers;

/// <summary>Everything a handler may use. Handlers never receive credentials or arbitrary paths.</summary>
public sealed record TaskExecutionContext(
    Guid TaskId,
    string TaskCode,
    string ProjectSlug,
    string WorkspacePath,
    string InputJson,
    int AttemptNumber,
    Action<string, string> Log);

public enum HandlerOutcome
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2
}

/// <summary>Standardized handler result; the runner maps it to complete/fail.</summary>
public sealed record TaskHandlerResult(
    HandlerOutcome Outcome,
    int ExitCode = 0,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool Retryable = false)
{
    public static TaskHandlerResult Succeeded(int exitCode = 0) => new(HandlerOutcome.Succeeded, exitCode);

    public static TaskHandlerResult Failed(string errorCode, string errorMessage, bool retryable = true)
        => new(HandlerOutcome.Failed, 1, errorCode, errorMessage, retryable);

    public static TaskHandlerResult Cancelled() => new(HandlerOutcome.Cancelled, 130, "cancelled", "Cancelled");
}

/// <summary>Controlled task execution abstraction. AOS-007 adds the Codex CLI implementation.</summary>
public interface ITaskHandler
{
    string Name { get; }

    Task<TaskHandlerResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken);
}
