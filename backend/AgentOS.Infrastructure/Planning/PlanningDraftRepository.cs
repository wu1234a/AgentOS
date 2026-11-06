using AgentOS.Application.Planning;
using AgentOS.Domain.Planning;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Planning;

/// <summary>EF Core implementation of planner drafts.</summary>
public sealed class PlanningDraftRepository(AgentOSDbContext dbContext) : IPlanningDraftRepository
{
    public Task<bool> ProjectEnabledAsync(Guid projectId, CancellationToken cancellationToken)
        => dbContext.Projects
            .AsNoTracking()
            .AnyAsync(project => project.Id == projectId && project.IsEnabled, cancellationToken);

    public async Task<(IReadOnlyList<PlanningDraftView> Items, int Total)> ListAsync(
        string? status,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PlanningDrafts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<PlanningDraftStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(draft => draft.Status == parsed);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(draft => draft.CreatedAtUtc)
            .ThenByDescending(draft => draft.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return (rows.Select(ToView).ToList(), total);
    }

    public Task<PlanningDraft?> GetTrackedAsync(Guid draftId, CancellationToken cancellationToken)
        => dbContext.PlanningDrafts.FirstOrDefaultAsync(draft => draft.Id == draftId, cancellationToken);

    public async Task<PlanningDraftView?> GetViewAsync(Guid draftId, CancellationToken cancellationToken)
    {
        var draft = await dbContext.PlanningDrafts
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == draftId, cancellationToken);

        return draft is null ? null : ToView(draft);
    }

    public async Task<bool> TryDecideAsync(
        Guid draftId,
        PlanningDraftStatus status,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var updated = await dbContext.PlanningDrafts
            .Where(draft => draft.Id == draftId && draft.Status == PlanningDraftStatus.PendingReview)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(draft => draft.Status, status)
                    .SetProperty(draft => draft.DecidedAtUtc, nowUtc)
                    .SetProperty(draft => draft.UpdatedAtUtc, nowUtc),
                cancellationToken);

        return updated > 0;
    }

    public async Task AddAsync(PlanningDraft draft, CancellationToken cancellationToken)
    {
        dbContext.PlanningDrafts.Add(draft);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private static PlanningDraftView ToView(PlanningDraft draft)
        => new(
            draft.Id,
            draft.ProjectId,
            draft.Title,
            draft.Goal,
            draft.Context,
            draft.ModelProfileId,
            draft.ModelProfileCode,
            draft.Status.ToString(),
            draft.ContentMarkdown,
            draft.ErrorMessage,
            draft.CreatedAtUtc,
            draft.UpdatedAtUtc,
            draft.DecidedAtUtc);
}
