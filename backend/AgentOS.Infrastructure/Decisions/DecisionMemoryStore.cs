using AgentOS.Application.Decisions;
using AgentOS.Domain.Approvals;
using AgentOS.Domain.Decisions;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Decisions;

public sealed class DecisionMemoryStore(AgentOSDbContext dbContext) : IDecisionMemoryStore
{
    public async Task RecordAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
        {
            return;
        }

        if (await dbContext.DecisionMemories.AnyAsync(
                memory => memory.SourceApprovalRequestId == approval.Id,
                cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow;
        dbContext.DecisionMemories.Add(new DecisionMemory
        {
            Id = Guid.CreateVersion7(),
            SourceApprovalRequestId = approval.Id,
            ScopeType = approval.TargetType.ToString(),
            ScopeKey = approval.TargetId,
            Decision = approval.Status == ApprovalStatus.Approved ? "Approved" : "Rejected",
            Reason = approval.DecisionReason,
            DecidedBy = approval.DecidedByUserId ?? "unknown",
            DecidedAtUtc = approval.DecidedAtUtc ?? now,
            ValidFromUtc = approval.DecidedAtUtc ?? now,
            ExpiresAtUtc = (approval.DecidedAtUtc ?? now).AddDays(90),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DecisionMemoryView>> ListAsync(
        string? decision,
        string? lifecycle,
        string? scopeType,
        string? scopeKey,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = Filter(decision, lifecycle, scopeType, scopeKey, DateTime.UtcNow);
        var rows = await query.OrderByDescending(memory => memory.DecidedAtUtc)
            .Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return rows.Select(ToView).ToList();
    }

    public Task<int> CountAsync(
        string? decision,
        string? lifecycle,
        string? scopeType,
        string? scopeKey,
        CancellationToken cancellationToken)
        => Filter(decision, lifecycle, scopeType, scopeKey, DateTime.UtcNow).CountAsync(cancellationToken);

    public async Task<DecisionMemoryView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var memory = await dbContext.DecisionMemories.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return memory is null ? null : ToView(memory);
    }

    public async Task<bool> InvalidateAsync(
        Guid id,
        string reason,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var memory = await dbContext.DecisionMemories.FirstOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (memory is null)
        {
            return false;
        }

        if (memory.InvalidatedAtUtc is null)
        {
            memory.InvalidatedAtUtc = nowUtc;
            memory.InvalidatedBy = "dev-user";
            memory.InvalidationReason = reason.Length <= 1000 ? reason : reason[..1000];
            memory.UpdatedAtUtc = nowUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<IReadOnlyList<DecisionMemoryView>> ReferencesAsync(
        string scopeType,
        string scopeKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var memories = await dbContext.DecisionMemories
            .Where(memory => memory.ScopeType == scopeType
                && memory.ScopeKey == scopeKey
                && memory.InvalidatedAtUtc == null
                && memory.ExpiresAtUtc > nowUtc)
            .OrderByDescending(memory => memory.DecidedAtUtc)
            .Take(5)
            .ToListAsync(cancellationToken);

        foreach (var memory in memories)
        {
            memory.HitCount++;
            memory.LastHitAtUtc = nowUtc;
        }

        if (memories.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return memories.Select(ToView).ToList();
    }

    private IQueryable<DecisionMemory> Filter(
        string? decision,
        string? lifecycle,
        string? scopeType,
        string? scopeKey,
        DateTime nowUtc)
    {
        var query = dbContext.DecisionMemories.AsNoTracking();
        if (Enum.TryParse<ApprovalStatus>(decision, true, out var decisionStatus))
        {
            query = query.Where(memory => memory.Decision == decisionStatus.ToString());
        }

        if (!string.IsNullOrWhiteSpace(scopeType))
        {
            query = query.Where(memory => memory.ScopeType == scopeType);
        }

        if (!string.IsNullOrWhiteSpace(scopeKey))
        {
            query = query.Where(memory => memory.ScopeKey == scopeKey);
        }

        query = lifecycle?.ToLowerInvariant() switch
        {
            "invalidated" => query.Where(memory => memory.InvalidatedAtUtc != null),
            "expired" => query.Where(memory => memory.InvalidatedAtUtc == null && memory.ExpiresAtUtc <= nowUtc),
            "active" => query.Where(memory => memory.InvalidatedAtUtc == null && memory.ExpiresAtUtc > nowUtc),
            _ => query
        };
        return query;
    }

    private static DecisionMemoryView ToView(DecisionMemory memory)
    {
        var lifecycle = memory.InvalidatedAtUtc is not null
            ? "Invalidated"
            : memory.ExpiresAtUtc <= DateTime.UtcNow ? "Expired" : "Active";
        return new DecisionMemoryView(
            memory.Id,
            memory.SourceApprovalRequestId,
            memory.ScopeType,
            memory.ScopeKey,
            memory.Decision,
            memory.Reason,
            memory.DecidedBy,
            memory.DecidedAtUtc,
            memory.ExpiresAtUtc,
            lifecycle,
            memory.HitCount,
            memory.LastHitAtUtc,
            memory.InvalidatedAtUtc,
            memory.InvalidationReason);
    }
}
