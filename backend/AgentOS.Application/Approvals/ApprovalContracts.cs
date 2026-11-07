using AgentOS.Domain.Approvals;

namespace AgentOS.Application.Approvals;

public sealed class CreateApprovalRequest
{
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? RequestReason { get; init; }
}

public sealed class ApprovalDecisionRequest
{
    public string? Decision { get; init; }
    public string? Reason { get; init; }
}

public sealed record ApprovalView(
    Guid Id,
    string TargetType,
    string TargetId,
    string Status,
    string? RequestReason,
    string RequestedByUserId,
    DateTime RequestedAtUtc,
    string? DecidedByUserId,
    DateTime? DecidedAtUtc,
    string? DecisionReason);

public enum ApprovalOutcome { Ok = 0, NotFound = 1, Conflict = 2, ValidationFailed = 3 }

public sealed record ApprovalResult(
    ApprovalOutcome Outcome,
    ApprovalView? Approval,
    string? ErrorCode,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static ApprovalResult Ok(ApprovalView value) => new(ApprovalOutcome.Ok, value, null, Empty);
    public static ApprovalResult NotFound() => new(ApprovalOutcome.NotFound, null, "approval-not-found", Empty);
    public static ApprovalResult Conflict(string code) => new(ApprovalOutcome.Conflict, null, code, Empty);
    public static ApprovalResult Invalid(string code, string field, string message)
        => new(ApprovalOutcome.ValidationFailed, null, code, new Dictionary<string, string[]> { [field] = [message] });
    private static readonly Dictionary<string, string[]> Empty = [];
}

public interface IApprovalRepository
{
    Task<bool> TargetExistsAsync(ApprovalTargetType type, string targetId, CancellationToken cancellationToken);
    Task<ApprovalRequest?> GetTrackedAsync(Guid id, CancellationToken cancellationToken);
    Task<ApprovalRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ApprovalRequest?> GetPendingAsync(ApprovalTargetType type, string targetId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApprovalRequest>> ListAsync(string? status, string? targetType, int limit, int offset, CancellationToken cancellationToken);
    Task<int> CountAsync(string? status, string? targetType, CancellationToken cancellationToken);
    Task AddAsync(ApprovalRequest request, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}
