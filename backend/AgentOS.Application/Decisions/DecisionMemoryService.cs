using AgentOS.Application.Decisions;

namespace AgentOS.Application.Decisions;

public sealed class DecisionMemoryService(IDecisionMemoryStore store)
{
    public Task<IReadOnlyList<DecisionMemoryView>> ListAsync(
        string? decision,
        string? lifecycle,
        string? scopeType,
        string? scopeKey,
        int limit,
        int offset,
        CancellationToken cancellationToken)
        => store.ListAsync(decision, lifecycle, scopeType, scopeKey, limit, offset, cancellationToken);

    public Task<int> CountAsync(
        string? decision,
        string? lifecycle,
        string? scopeType,
        string? scopeKey,
        CancellationToken cancellationToken)
        => store.CountAsync(decision, lifecycle, scopeType, scopeKey, cancellationToken);

    public Task<DecisionMemoryView?> GetAsync(Guid id, CancellationToken cancellationToken)
        => store.GetAsync(id, cancellationToken);

    public Task<bool> InvalidateAsync(Guid id, string reason, CancellationToken cancellationToken)
        => store.InvalidateAsync(id, reason, DateTime.UtcNow, cancellationToken);

    public Task<IReadOnlyList<DecisionMemoryView>> ReferencesAsync(
        string scopeType,
        string scopeKey,
        CancellationToken cancellationToken)
        => store.ReferencesAsync(scopeType, scopeKey, DateTime.UtcNow, cancellationToken);
}
