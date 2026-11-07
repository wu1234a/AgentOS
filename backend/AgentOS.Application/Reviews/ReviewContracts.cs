using AgentOS.Domain.Reviews;

namespace AgentOS.Application.Reviews;

public sealed record ReviewFindingView(
    string Severity,
    string FilePath,
    int? StartLine,
    int? EndLine,
    string Reason,
    string Suggestion);

public sealed record ReviewView(
    Guid Id,
    Guid CodingRunId,
    string? BaseRevision,
    string? HeadRevision,
    string Status,
    string? Conclusion,
    string? Summary,
    string? ErrorCode,
    string? ErrorMessage,
    int AttemptCount,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<ReviewFindingView> Findings);

public sealed record ReviewAgentFinding(
    string Severity,
    string FilePath,
    int? StartLine,
    int? EndLine,
    string Reason,
    string Suggestion);

public sealed record ReviewAgentRequest(
    string Diff,
    IReadOnlyList<string> Files,
    string? BaseRevision,
    string? HeadRevision);

public sealed record ReviewAgentResult(
    bool Success,
    string? Summary,
    string? Conclusion,
    IReadOnlyList<ReviewAgentFinding> Findings,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record ReviewChangeSet(
    string Diff,
    IReadOnlyList<string> Files,
    string? BaseRevision,
    string? HeadRevision);

public interface IReviewAgent
{
    Task<ReviewAgentResult> ReviewAsync(
        ReviewAgentRequest request,
        CancellationToken cancellationToken);
}

public enum ReviewOutcome
{
    Ok = 0,
    NotFound = 1,
    Conflict = 2
}

public sealed record ReviewResult(
    ReviewOutcome Outcome,
    ReviewView? Review,
    string? ErrorCode)
{
    public static ReviewResult Ok(ReviewView review) => new(ReviewOutcome.Ok, review, null);

    public static ReviewResult NotFound() => new(ReviewOutcome.NotFound, null, "review-not-found");

    public static ReviewResult Conflict(string code) => new(ReviewOutcome.Conflict, null, code);
}

public interface IReviewRepository
{
    Task<ReviewRun?> GetTrackedAsync(Guid codingRunId, CancellationToken cancellationToken);

    Task<ReviewView?> GetViewAsync(Guid codingRunId, CancellationToken cancellationToken);

    Task<ReviewChangeSet?> GetChangeSetAsync(Guid taskId, CancellationToken cancellationToken);

    Task AddReviewAsync(ReviewRun review, CancellationToken cancellationToken);

    Task AddFindingsAsync(IReadOnlyList<ReviewFinding> findings, CancellationToken cancellationToken);

    Task DeleteFindingsAsync(Guid reviewId, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
