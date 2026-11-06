namespace AgentOS.Runner;

/// <summary>One validation command from an approved Coding Run snapshot.</summary>
public sealed record ValidationCommand(string Command, int TimeoutSeconds);

public sealed record ValidationCommandResult(
    bool Success,
    int? ExitCode,
    string Output,
    string? ErrorCode,
    string? ErrorMessage);

public interface IValidationCommandRunner
{
    Task<ValidationCommandResult> RunAsync(
        string workspacePath,
        ValidationCommand command,
        Action<string, string> log,
        CancellationToken cancellationToken);
}
