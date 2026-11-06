using AgentOS.Application.CodingRuns;
using AgentOS.Domain.CodingRuns;
using AgentOS.Domain.Planning;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Infrastructure.CodingRuns;

public sealed class CodingRunRepository(AgentOSDbContext dbContext) : ICodingRunRepository
{
    public Task<PlanningDraft?> GetDraftAsync(Guid draftId, CancellationToken cancellationToken)
        => dbContext.PlanningDrafts
            .AsNoTracking()
            .FirstOrDefaultAsync(draft => draft.Id == draftId, cancellationToken);

    public Task<bool> ProjectEnabledAsync(Guid projectId, CancellationToken cancellationToken)
        => dbContext.Projects
            .AsNoTracking()
            .AnyAsync(project => project.Id == projectId && project.IsEnabled, cancellationToken);

    public Task<bool> AgentEnabledAsync(
        Guid agentId,
        Guid projectId,
        CancellationToken cancellationToken)
        => dbContext.Agents
            .AsNoTracking()
            .AnyAsync(
                agent => agent.Id == agentId
                    && agent.ProjectId == projectId
                    && agent.IsEnabled,
                cancellationToken);

    public Task<CodingRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
        => dbContext.CodingRuns.FirstOrDefaultAsync(run => run.Id == runId, cancellationToken);

    public Task<CodingRun?> GetRunByDraftAsync(Guid draftId, CancellationToken cancellationToken)
        => dbContext.CodingRuns.FirstOrDefaultAsync(run => run.DraftId == draftId, cancellationToken);

    public Task<AutomationTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken)
        => dbContext.Tasks.FirstOrDefaultAsync(task => task.Id == taskId, cancellationToken);

    public async Task<IReadOnlyList<CodingRun>> ListRunsForSyncAsync(
        int limit,
        CancellationToken cancellationToken)
        => await dbContext.CodingRuns
            .Where(run => run.TaskId != null
                && run.Status != CodingRunStatus.Succeeded
                && run.Status != CodingRunStatus.Failed
                && run.Status != CodingRunStatus.Canceled)
            .OrderBy(run => run.UpdatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<CodingRunView> Items, int Total)> ListAsync(
        string? status,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = dbContext.CodingRuns.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<CodingRunStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(run => run.Status == parsed);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await (
            from run in query
            join draft in dbContext.PlanningDrafts.AsNoTracking() on run.DraftId equals draft.Id
            join task in dbContext.Tasks.AsNoTracking() on run.TaskId equals task.Id into tasks
            from task in tasks.DefaultIfEmpty()
            orderby run.CreatedAtUtc descending, run.Id descending
            select new { run, draft.Title, TaskStatus = task == null ? null : (TaskStatus?)task.Status })
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new CodingRunView(
                row.run.Id,
                row.run.DraftId,
                row.Title,
                row.run.ProjectId,
                row.run.AgentId,
                row.run.TaskId,
                row.run.Status.ToString(),
                row.TaskStatus?.ToString(),
                row.run.AttemptCount,
                row.run.DraftContentHash,
                row.run.ErrorCode,
                row.run.ErrorMessage,
                row.run.CreatedAtUtc,
                row.run.UpdatedAtUtc,
                row.run.StartedAtUtc,
                row.run.CompletedAtUtc,
                row.run.CancelRequestedAtUtc))
            .ToList();

        return (items, total);
    }

    public async Task AddRunAsync(CodingRun run, CancellationToken cancellationToken)
        => await dbContext.CodingRuns.AddAsync(run, cancellationToken);

    public async Task AddTaskAsync(AutomationTask task, CancellationToken cancellationToken)
        => await dbContext.Tasks.AddAsync(task, cancellationToken);

    public async Task AddEventAsync(CodingRunEvent evt, CancellationToken cancellationToken)
        => await dbContext.CodingRunEvents.AddAsync(evt, cancellationToken);

    public async Task<IReadOnlyList<CodingRunEventView>> ListEventsAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var events = await dbContext.CodingRunEvents
            .AsNoTracking()
            .Where(evt => evt.CodingRunId == runId)
            .OrderBy(evt => evt.Sequence)
            .ToListAsync(cancellationToken);

        return events
            .Select(evt => new CodingRunEventView(
                evt.Sequence,
                evt.EventType,
                evt.FromStatus,
                evt.ToStatus,
                evt.DetailJson,
                evt.CreatedAtUtc))
            .ToList();
    }

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
