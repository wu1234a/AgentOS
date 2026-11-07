using AgentOS.Domain.Tasks;

namespace AgentOS.Application.Tasks;

public sealed class CreateSubtaskRequest
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? PhaseKey { get; init; }

    public Guid[]? DependsOnTaskIds { get; init; }
}

public sealed record SubtaskView(
    Guid Id,
    Guid ParentTaskId,
    Guid ProjectId,
    string Title,
    string? Description,
    string? PhaseKey,
    string Status,
    IReadOnlyList<Guid> DependsOnTaskIds,
    IReadOnlyList<Guid> UnresolvedDependencyIds,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record SubtaskSummary(
    int Total,
    int Queued,
    int Running,
    int Blocked,
    int Succeeded,
    int Failed,
    int Other,
    bool AllCompleted);

public sealed record SubtaskListResult(
    Guid ParentTaskId,
    string ParentStatus,
    IReadOnlyList<SubtaskView> Items,
    SubtaskSummary Summary);

public enum SubtaskOutcome
{
    Ok = 0,
    NotFound = 1,
    Conflict = 2,
    ValidationFailed = 3
}

public sealed record SubtaskResult(
    SubtaskOutcome Outcome,
    SubtaskView? Subtask,
    string? ErrorCode,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static SubtaskResult Ok(SubtaskView view) => new(SubtaskOutcome.Ok, view, null, Empty);

    public static SubtaskResult NotFound() => new(SubtaskOutcome.NotFound, null, "task-not-found", Empty);

    public static SubtaskResult Conflict(string code) => new(SubtaskOutcome.Conflict, null, code, Empty);

    public static SubtaskResult Invalid(string code, string field, string message)
        => new(SubtaskOutcome.ValidationFailed, null, code, new Dictionary<string, string[]>
        {
            [field] = [message]
        });

    private static readonly Dictionary<string, string[]> Empty = [];
}
