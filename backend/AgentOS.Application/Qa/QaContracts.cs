using AgentOS.Domain.Qa;

namespace AgentOS.Application.Qa;

public sealed record QaCommandView(
    int Ordinal,
    string CommandText,
    string Status,
    int? ExitCode,
    int? DurationMs,
    string? OutputExcerpt);

public sealed record QaView(
    Guid Id,
    Guid CodingRunId,
    int AttemptNo,
    string Status,
    string? Conclusion,
    int CommandCount,
    int PassedCount,
    int FailedCount,
    string? ErrorSummary,
    DateTime CreatedAtUtc,
    IReadOnlyList<QaCommandView> Commands);

public sealed record ValidationCommandRecord(
    string Command,
    string Status,
    int? ExitCode,
    int? DurationMs,
    string? Output);

public interface IQaRepository
{
    Task<QaView?> GetLatestAsync(Guid codingRunId, CancellationToken cancellationToken);

    Task<int> NextAttemptAsync(Guid codingRunId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ValidationCommandRecord>?> GetValidationResultsAsync(
        Guid taskId,
        CancellationToken cancellationToken);

    Task AddAsync(QaRun run, IReadOnlyList<QaCommandResult> commands, CancellationToken cancellationToken);
}

public enum QaOutcome
{
    Ok = 0,
    NotFound = 1,
    Conflict = 2
}

public sealed record QaResult(QaOutcome Outcome, QaView? Qa, string? ErrorCode)
{
    public static QaResult Ok(QaView qa) => new(QaOutcome.Ok, qa, null);

    public static QaResult NotFound() => new(QaOutcome.NotFound, null, "qa-not-found");

    public static QaResult Conflict(string code) => new(QaOutcome.Conflict, null, code);
}
