using AgentOS.Application.Approvals;
using AgentOS.Domain.Approvals;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Approvals;

public sealed class ApprovalRepository(AgentOSDbContext dbContext) : IApprovalRepository
{
    public async Task<bool> TargetExistsAsync(
        ApprovalTargetType type,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id))
        {
            return false;
        }

        return type switch
        {
            ApprovalTargetType.CodingRun => await dbContext.CodingRuns.AnyAsync(run => run.Id == id, cancellationToken),
            ApprovalTargetType.PlannerDraft => await dbContext.PlanningDrafts.AnyAsync(draft => draft.Id == id, cancellationToken),
            _ => false
        };
    }

    public Task<ApprovalRequest?> GetTrackedAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.ApprovalRequests.FirstOrDefaultAsync(request => request.Id == id, cancellationToken);

    public Task<ApprovalRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.ApprovalRequests.AsNoTracking().FirstOrDefaultAsync(request => request.Id == id, cancellationToken);

    public Task<ApprovalRequest?> GetPendingAsync(
        ApprovalTargetType type,
        string targetId,
        CancellationToken cancellationToken)
        => dbContext.ApprovalRequests.FirstOrDefaultAsync(
            request => request.TargetType == type
                && request.TargetId == targetId
                && request.Status == ApprovalStatus.Pending,
            cancellationToken);

    public async Task<IReadOnlyList<ApprovalRequest>> ListAsync(
        string? status,
        string? targetType,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = Filter(status, targetType);
        return await query
            .OrderByDescending(request => request.RequestedAtUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(string? status, string? targetType, CancellationToken cancellationToken)
        => Filter(status, targetType).CountAsync(cancellationToken);

    public async Task<bool> TryAddAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        dbContext.ApprovalRequests.Add(request);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(request).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryDecideAsync(
        Guid id,
        ApprovalStatus status,
        string decidedBy,
        string? reason,
        DateTime decidedAtUtc,
        CancellationToken cancellationToken)
    {
        var updated = await dbContext.ApprovalRequests
            .Where(request => request.Id == id && request.Status == ApprovalStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.Status, status)
                .SetProperty(request => request.DecidedByUserId, decidedBy)
                .SetProperty(request => request.DecidedAtUtc, decidedAtUtc)
                .SetProperty(request => request.DecisionReason, reason), cancellationToken);
        return updated > 0;
    }

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<ApprovalRequest> Filter(string? status, string? targetType)
    {
        var query = dbContext.ApprovalRequests.AsNoTracking();
        if (Enum.TryParse<ApprovalStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(request => request.Status == parsedStatus);
        }

        if (Enum.TryParse<ApprovalTargetType>(targetType, true, out var parsedType))
        {
            query = query.Where(request => request.TargetType == parsedType);
        }

        return query;
    }
}
