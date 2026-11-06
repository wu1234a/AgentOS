using AgentOS.Domain.CodingRuns;
using AgentOS.Domain.Planning;
using AgentOS.Domain.Tasks;

namespace AgentOS.Application.CodingRuns;

public sealed class CreateCodingRunRequest
{
    public Guid DraftId { get; init; }

    public Guid ProjectId { get; init; }

    public Guid AgentId { get; init; }

    public string? Priority { get; init; }

    public int? TimeoutSeconds { get; init; }
}

public sealed record CodingRunView(
    Guid Id,
    Guid DraftId,
    string Title,
    Guid ProjectId,
    Guid AgentId,
    Guid? TaskId,
    string Status,
    string? TaskStatus,
    int AttemptCount,
    string DraftContentHash,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelRequestedAtUtc);

public sealed record CodingRunEventView(
    int Sequence,
    string EventType,
    string? FromStatus,
    string ToStatus,
    string? DetailJson,
    DateTime CreatedAtUtc);

public sealed record CodingRunDetail(CodingRunView Run, IReadOnlyList<CodingRunEventView> Events);

public enum CodingRunOutcome
{
    Ok = 0,
    NotFound = 1,
    Conflict = 2,
    ValidationFailed = 3
}

public sealed record CodingRunResult(
    CodingRunOutcome Outcome,
    CodingRunView? Run,
    string? ErrorCode,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static CodingRunResult Ok(CodingRunView run) => new(CodingRunOutcome.Ok, run, null, Empty);

    public static CodingRunResult NotFound() => new(CodingRunOutcome.NotFound, null, "coding-run-not-found", Empty);

    public static CodingRunResult Conflict(string code) => new(CodingRunOutcome.Conflict, null, code, Empty);

    public static CodingRunResult Invalid(string code, IReadOnlyDictionary<string, string[]> errors)
        => new(CodingRunOutcome.ValidationFailed, null, code, errors);

    private static readonly Dictionary<string, string[]> Empty = [];
}

public interface ICodingRunRepository
{
    Task<PlanningDraft?> GetDraftAsync(Guid draftId, CancellationToken cancellationToken);

    Task<bool> ProjectEnabledAsync(Guid projectId, CancellationToken cancellationToken);

    Task<bool> AgentEnabledAsync(Guid agentId, Guid projectId, CancellationToken cancellationToken);

    Task<CodingRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken);

    Task<CodingRun?> GetRunByDraftAsync(Guid draftId, CancellationToken cancellationToken);

    Task<AutomationTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CodingRun>> ListRunsForSyncAsync(int limit, CancellationToken cancellationToken);

    Task<(IReadOnlyList<CodingRunView> Items, int Total)> ListAsync(
        string? status,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task AddRunAsync(CodingRun run, CancellationToken cancellationToken);

    Task AddTaskAsync(AutomationTask task, CancellationToken cancellationToken);

    Task AddEventAsync(CodingRunEvent evt, CancellationToken cancellationToken);

    Task<IReadOnlyList<CodingRunEventView>> ListEventsAsync(
        Guid runId,
        CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
