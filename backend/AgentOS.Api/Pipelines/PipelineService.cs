using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.Pipelines;

public sealed record PipelineStage(string Name, string Status, DateTime? StartedAtUtc, DateTime? CompletedAtUtc, string? Detail);

public sealed record PipelineView(
    Guid TaskId,
    string TaskCode,
    string Title,
    Guid ProjectId,
    string TaskStatus,
    string? RuleId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<PipelineStage> Stages);

public sealed record PipelineSummary(
    Guid TaskId,
    string TaskCode,
    string Title,
    Guid ProjectId,
    string TaskStatus,
    DateTime UpdatedAtUtc,
    int AttemptCount,
    bool HasCodingRun);

public sealed class PipelineService(AgentOSDbContext dbContext)
{
    public async Task<PipelineView?> GetAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await dbContext.Tasks.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var stages = new List<PipelineStage>
        {
            new("task", task.Status.ToString(), task.CreatedAtUtc, task.CompletedAtUtc, task.ErrorMessage)
        };

        var attempts = await dbContext.TaskAttempts.AsNoTracking()
            .Where(attempt => attempt.TaskId == taskId)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToListAsync(cancellationToken);
        foreach (var attempt in attempts)
        {
            stages.Add(new PipelineStage(
                $"attempt-{attempt.AttemptNumber}", attempt.Status.ToString(), attempt.StartedAtUtc,
                attempt.FinishedAtUtc, attempt.ErrorMessage));
        }

        var codingRuns = await dbContext.CodingRuns.AsNoTracking()
            .Where(run => run.TaskId == taskId)
            .OrderBy(run => run.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        foreach (var run in codingRuns)
        {
            stages.Add(new PipelineStage("coding-run", task.Status.ToString(), run.StartedAtUtc, run.CompletedAtUtc, run.ErrorMessage));
            var reviews = await dbContext.ReviewRuns.AsNoTracking()
                .Where(review => review.CodingRunId == run.Id)
                .ToListAsync(cancellationToken);
            stages.AddRange(reviews.Select(review => new PipelineStage(
                "review", review.Status.ToString(), review.StartedAtUtc, review.CompletedAtUtc,
                $"{review.Conclusion}: {review.Summary}")));
            var qaRuns = await dbContext.QaRuns.AsNoTracking()
                .Where(qa => qa.CodingRunId == run.Id)
                .ToListAsync(cancellationToken);
            stages.AddRange(qaRuns.Select(qa => new PipelineStage(
                $"qa-{qa.AttemptNo}", qa.Status.ToString(), null, null,
                $"{qa.Conclusion}: {qa.ErrorSummary}")));
        }

        return new PipelineView(task.Id, task.TaskCode, task.Title, task.ProjectId, task.Status.ToString(),
            task.RuleId?.ToString(), task.CreatedAtUtc, task.UpdatedAtUtc, stages);
    }

    public async Task<IReadOnlyList<PipelineSummary>> ListAsync(Guid? projectId, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Tasks.AsNoTracking();
        if (projectId is Guid id)
        {
            query = query.Where(task => task.ProjectId == id);
        }

        var tasks = await query.OrderByDescending(task => task.UpdatedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
        var ids = tasks.Select(task => task.Id).ToList();
        var attemptCounts = await dbContext.TaskAttempts.AsNoTracking()
            .Where(attempt => ids.Contains(attempt.TaskId))
            .GroupBy(attempt => attempt.TaskId)
            .Select(group => new { TaskId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.TaskId, row => row.Count, cancellationToken);
        var codingTaskIds = await dbContext.CodingRuns.AsNoTracking()
            .Where(run => run.TaskId != null && ids.Contains(run.TaskId.Value))
            .Select(run => run.TaskId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        return tasks.Select(task => new PipelineSummary(
            task.Id, task.TaskCode, task.Title, task.ProjectId, task.Status.ToString(), task.UpdatedAtUtc,
            attemptCounts.GetValueOrDefault(task.Id), codingTaskIds.Contains(task.Id))).ToList();
    }
}
