using AgentOS.Domain.Approvals;

namespace AgentOS.Application.Decisions;

public sealed record DecisionMemoryView(
    Guid Id,
    Guid SourceApprovalRequestId,
    string ScopeType,
    string ScopeKey,
    string Decision,
    string? Reason,
    string DecidedBy,
    DateTime DecidedAtUtc,
    DateTime ExpiresAtUtc,
    string Lifecycle,
    int HitCount,
    DateTime? LastHitAtUtc,
    DateTime? InvalidatedAtUtc,
    string? InvalidationReason);

public interface IDecisionMemoryRecorder
{
    Task RecordAsync(ApprovalRequest approval, CancellationToken cancellationToken);
}

public interface IDecisionMemoryStore : IDecisionMemoryRecorder
{
    Task<IReadOnlyList<DecisionMemoryView>> ListAsync(string? decision, string? lifecycle, string? scopeType, string? scopeKey, int limit, int offset, CancellationToken cancellationToken);
    Task<int> CountAsync(string? decision, string? lifecycle, string? scopeType, string? scopeKey, CancellationToken cancellationToken);
    Task<DecisionMemoryView?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> InvalidateAsync(Guid id, string reason, DateTime nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<DecisionMemoryView>> ReferencesAsync(string scopeType, string scopeKey, DateTime nowUtc, CancellationToken cancellationToken);
}
