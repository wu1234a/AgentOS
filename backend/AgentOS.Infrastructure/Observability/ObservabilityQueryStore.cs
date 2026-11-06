using AgentOS.Application.Observability;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Infrastructure.Observability;

/// <summary>EF Core read model for the management UI.</summary>
public sealed class ObservabilityQueryStore(AgentOSDbContext dbContext) : IObservabilityQueryStore
{
    public async Task<DashboardStats> GetDashboardAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var totalEvents = await dbContext.Events.CountAsync(cancellationToken);
        var duplicateEvents = await dbContext.Events.SumAsync(evt => evt.DuplicateCount, cancellationToken);

        var totalTasks = await dbContext.Tasks.CountAsync(cancellationToken);
        var queued = await dbContext.Tasks.CountAsync(task => task.Status == TaskStatus.Queued, cancellationToken);
        var running = await dbContext.Tasks.CountAsync(task => task.Status == TaskStatus.Running, cancellationToken);
        var succeeded = await dbContext.Tasks.CountAsync(task => task.Status == TaskStatus.Succeeded, cancellationToken);
        var failed = await dbContext.Tasks.CountAsync(task => task.Status == TaskStatus.Failed, cancellationToken);
        var cancelled = await dbContext.Tasks.CountAsync(task => task.Status == TaskStatus.Cancelled, cancellationToken);

        var terminal = succeeded + failed + cancelled;
        var successRate = terminal == 0 ? 0d : Math.Round(succeeded * 100d / terminal, 1);

        var averageWaitMs = await dbContext.Tasks
            .Where(task => task.StartedAtUtc != null)
            .AverageAsync(
                task => (double?)EF.Functions.DateDiffMillisecond(task.CreatedAtUtc, task.StartedAtUtc!.Value),
                cancellationToken);

        var averageExecutionMs = await dbContext.Tasks
            .Where(task => task.StartedAtUtc != null && task.CompletedAtUtc != null)
            .AverageAsync(
                task => (double?)EF.Functions.DateDiffMillisecond(task.StartedAtUtc!.Value, task.CompletedAtUtc!.Value),
                cancellationToken);

        var onlineRunners = await dbContext.Workers.CountAsync(
            worker => worker.Status == WorkerStatus.Online && worker.LastHeartbeatAtUtc >= nowUtc.AddMinutes(-2),
            cancellationToken);

        var leaseExpiries = await dbContext.TaskAttempts.CountAsync(
            attempt => attempt.Status == AttemptStatus.Lost,
            cancellationToken);

        var failureRows = await TaskRows(
                dbContext.Tasks.AsNoTracking()
                    .Where(task => task.Status == TaskStatus.Failed)
                    .OrderByDescending(task => task.CompletedAtUtc)
                    .Take(5))
            .ToListAsync(cancellationToken);

        return new DashboardStats(
            totalEvents,
            duplicateEvents,
            totalTasks,
            queued,
            running,
            succeeded,
            failed,
            cancelled,
            successRate,
            averageWaitMs is null ? null : Math.Round(averageWaitMs.Value / 1000d, 1),
            averageExecutionMs is null ? null : Math.Round(averageExecutionMs.Value / 1000d, 1),
            onlineRunners,
            leaseExpiries,
            failureRows.Select(ToSummary).ToList());
    }

    public async Task<(IReadOnlyList<EventListItem> Items, int Total)> ListEventsAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var total = await dbContext.Events.CountAsync(cancellationToken);

        var rows = await (from evt in dbContext.Events.AsNoTracking()
                          join task in dbContext.Tasks on evt.Id equals task.EventId into tasks
                          orderby evt.ReceivedAtUtc descending
                          select new EventRow(evt, tasks.Count()))
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return (rows.Select(ToEventItem).ToList(), total);
    }

    public async Task<EventDetail?> GetEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var evt = await dbContext.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken);

        if (evt is null)
        {
            return null;
        }

        var rows = await TaskRows(
                dbContext.Tasks.AsNoTracking()
                    .Where(task => task.EventId == eventId)
                    .OrderByDescending(task => task.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new EventDetail(
            new EventListItem(
                evt.Id,
                evt.Source,
                evt.EventType,
                evt.Status.ToString(),
                evt.DedupeKey,
                evt.OccurredAtUtc,
                evt.ReceivedAtUtc,
                rows.Count),
            evt.PayloadJson,
            rows.Select(ToSummary).ToList());
    }

    public async Task<(IReadOnlyList<TaskSummary> Items, int Total)> ListTasksAsync(
        string? status,
        Guid? projectId,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Tasks.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TaskStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(task => task.Status == parsed);
        }

        if (projectId is not null)
        {
            query = query.Where(task => task.ProjectId == projectId);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await TaskRows(
                query.OrderByDescending(task => task.CreatedAtUtc).Skip(offset).Take(limit))
            .ToListAsync(cancellationToken);

        return (rows.Select(ToSummary).ToList(), total);
    }

    public async Task<TaskDetail?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var row = await TaskRows(dbContext.Tasks.AsNoTracking().Where(task => task.Id == taskId))
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var attemptRows = await (from attempt in dbContext.TaskAttempts.AsNoTracking()
                                 join worker in dbContext.Workers on attempt.WorkerId equals worker.Id
                                 where attempt.TaskId == taskId
                                 orderby attempt.AttemptNumber
                                 select new
                                 {
                                     attempt,
                                     WorkerCode = worker.WorkerCode
                                 })
            .ToListAsync(cancellationToken);

        var artifacts = await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.TaskId == taskId)
            .OrderBy(artifact => artifact.CreatedAtUtc)
            .Select(artifact => new ArtifactView(
                artifact.Id,
                artifact.Name,
                artifact.MediaType,
                artifact.SizeBytes,
                artifact.Sha256,
                artifact.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var usage = await dbContext.UsageRecords
            .AsNoTracking()
            .Where(record => record.TaskId == taskId)
            .OrderByDescending(record => record.CreatedAtUtc)
            .Select(record => new UsageView(
                record.Provider,
                record.Model,
                record.InputTokens,
                record.CachedInputTokens,
                record.OutputTokens,
                record.ReasoningTokens,
                record.TotalTokens,
                record.Source))
            .FirstOrDefaultAsync(cancellationToken);

        var logs = await dbContext.TaskLogs
            .AsNoTracking()
            .Where(log => log.TaskId == taskId)
            .OrderByDescending(log => log.Sequence)
            .Take(50)
            .Select(log => new TaskLogView(
                log.Id,
                log.Sequence,
                log.Level,
                log.Component,
                log.Message,
                log.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        logs.Reverse();

        var attempts = attemptRows
            .Select(item => new AttemptView(
                item.attempt.Id,
                item.attempt.AttemptNumber,
                item.attempt.WorkerId,
                item.WorkerCode,
                item.attempt.Status.ToString(),
                item.attempt.StartedAtUtc,
                item.attempt.FinishedAtUtc,
                item.attempt.ExitCode,
                item.attempt.ErrorCode,
                item.attempt.ErrorMessage,
                item.attempt.DurationMs))
            .ToList();

        return new TaskDetail(ToSummary(row), attempts, artifacts, usage, logs);
    }

    public Task<LogPage> GetTaskLogsAsync(
        Guid taskId,
        string? level,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken)
        => QueryLogsAsync(log => log.TaskId == taskId, level, afterSequence, limit, cancellationToken);

    public Task<LogPage> GetAttemptLogsAsync(
        Guid taskId,
        Guid attemptId,
        string? level,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken)
        => QueryLogsAsync(
            log => log.TaskId == taskId && log.AttemptId == attemptId,
            level,
            afterSequence,
            limit,
            cancellationToken);

    public async Task<IReadOnlyList<WorkerView>> ListWorkersAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.Workers
            .AsNoTracking()
            .OrderByDescending(worker => worker.LastHeartbeatAtUtc)
            .ToListAsync(cancellationToken);

        return rows
            .Select(worker => new WorkerView(
                worker.Id,
                worker.WorkerCode,
                worker.DisplayName,
                worker.Status.ToString(),
                worker.Platform,
                worker.Architecture,
                worker.Version,
                worker.LastHeartbeatAtUtc,
                worker.RegisteredAtUtc,
                worker.CapabilitiesJson))
            .ToList();
    }

    private async Task<LogPage> QueryLogsAsync(
        System.Linq.Expressions.Expression<Func<TaskLog, bool>> filter,
        string? level,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.TaskLogs.AsNoTracking().Where(filter);

        if (!string.IsNullOrWhiteSpace(level))
        {
            var normalized = level.Trim().ToLowerInvariant();
            query = query.Where(log => log.Level == normalized);
        }

        if (afterSequence > 0)
        {
            query = query.Where(log => log.Sequence > afterSequence);
        }

        var items = await query
            .OrderBy(log => log.Sequence)
            .Take(limit + 1)
            .Select(log => new TaskLogView(
                log.Id,
                log.Sequence,
                log.Level,
                log.Component,
                log.Message,
                log.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        int? nextSequence = null;
        if (items.Count > limit)
        {
            items.RemoveAt(items.Count - 1);
            nextSequence = items[^1].Sequence;
        }

        return new LogPage(items, nextSequence);
    }

    private IQueryable<TaskRow> TaskRows(IQueryable<AutomationTask>? source = null)
    {
        var tasks = source ?? dbContext.Tasks.AsNoTracking();

        return from task in tasks
               join project in dbContext.Projects on task.ProjectId equals project.Id
               join agent in dbContext.Agents on task.AgentId equals agent.Id
               join skill in dbContext.Skills on task.SkillId equals skill.Id into skills
               from skill in skills.DefaultIfEmpty()
               select new TaskRow(
                   task.Id,
                   task.TaskCode,
                   task.Title,
                   task.Status,
                   task.Priority,
                   project.Slug,
                   agent.Code,
                   skill == null ? null : skill.Code,
                   task.CreatedAtUtc,
                   task.StartedAtUtc,
                   task.CompletedAtUtc,
                   task.AttemptCount,
                   task.MaxAttempts,
                   task.EventId);
    }

    private static TaskSummary ToSummary(TaskRow row)
        => new(
            row.Id,
            row.TaskCode,
            row.Title,
            row.Status.ToString(),
            row.Priority,
            row.ProjectSlug,
            row.AgentCode,
            row.SkillCode,
            row.CreatedAtUtc,
            row.StartedAtUtc,
            row.CompletedAtUtc,
            row.AttemptCount,
            row.MaxAttempts);

    private static EventListItem ToEventItem(EventRow row)
        => new(
            row.Event.Id,
            row.Event.Source,
            row.Event.EventType,
            row.Event.Status.ToString(),
            row.Event.DedupeKey,
            row.Event.OccurredAtUtc,
            row.Event.ReceivedAtUtc,
            row.TaskCount);

    private sealed record EventRow(Domain.Events.Event Event, int TaskCount);

    private sealed record TaskRow(
        Guid Id,
        string TaskCode,
        string Title,
        TaskStatus Status,
        string Priority,
        string ProjectSlug,
        string AgentCode,
        string? SkillCode,
        DateTime CreatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc,
        int AttemptCount,
        int MaxAttempts,
        Guid? EventId);
}
