namespace AgentOS.AgentAdapters;

public enum AgentExecutionStatus
{
    Succeeded = 0,
    Failed = 1,
    TimedOut = 2,
    Cancelled = 3,

    /// <summary>The CLI is missing, misconfigured, or refused to start.</summary>
    Unavailable = 4
}

public enum AgentSandboxMode
{
    ReadOnly = 0,
    WorkspaceWrite = 1
}

/// <summary>Execution policy the adapter must honour. The adapter never widens it.</summary>
public sealed record AgentExecutionPolicy(
    AgentSandboxMode SandboxMode,
    IReadOnlyList<string> EnvironmentAllowList,
    int MaxOutputBytes = 2_000_000);

public sealed record AgentExecutionRequest(
    Guid TaskId,
    Guid AttemptId,
    string ProjectCode,
    string WorkingDirectory,
    string Prompt,
    int TimeoutSeconds,
    AgentExecutionPolicy Policy,
    Action<string, string>? OnLog = null,
    string? Model = null);

public sealed record AgentExecutionResult(
    AgentExecutionStatus Status,
    int? ExitCode,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc,
    string? StdoutArtifactPath,
    string? StderrArtifactPath,
    string? Summary,
    string? ErrorCode,
    string? ErrorMessage,
    string? ExternalSessionId);

/// <summary>Contract for agent execution adapters (Codex CLI first, but not the only one).</summary>
public interface IAgentAdapter
{
    /// <summary>Stable adapter code, for example <c>codex-cli</c>.</summary>
    string Code { get; }

    Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken);
}
