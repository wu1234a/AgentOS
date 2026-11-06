using AgentOS.Application.Rules;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Events;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentOS.Infrastructure.Tasks;

/// <summary>EF Core implementation of the dispatch transaction.</summary>
public sealed class TaskDispatchStore(AgentOSDbContext dbContext, ILogger<TaskDispatchStore> logger)
    : ITaskDispatchStore
{
    public async Task<IReadOnlyList<RuleSnapshot>> LoadEnabledRulesAsync(CancellationToken cancellationToken)
    {
        // Only rules whose agent and optional skill/model profile are enabled may dispatch work.
        var rules = await dbContext.AutomationRules
            .AsNoTracking()
            .Where(rule => rule.IsEnabled
                           && dbContext.Agents.Any(agent => agent.Id == rule.AgentId && agent.IsEnabled)
                           && (rule.SkillId == null
                               || dbContext.Skills.Any(skill => skill.Id == rule.SkillId && skill.IsEnabled))
                           && (rule.ModelProfileId == null
                               || dbContext.ModelProfiles.Any(profile =>
                                   profile.Id == rule.ModelProfileId && profile.IsEnabled)))
            .ToListAsync(cancellationToken);

        var snapshots = new List<RuleSnapshot>(rules.Count);
        foreach (var rule in rules)
        {
            if (!RuleDispatchSettings.TryParse(rule.DispatchJson, out var dispatch, out var dispatchError))
            {
                logger.LogWarning(
                    "Rule {RuleId} is enabled but its dispatchJson is invalid and will be skipped: {Error}",
                    rule.Id,
                    dispatchError);
                continue;
            }

            if (!RuleMatchConditions.TryParse(rule.MatchJson, out var conditions, out var matchError))
            {
                logger.LogWarning(
                    "Rule {RuleId} is enabled but its matchJson is invalid and will be skipped: {Error}",
                    rule.Id,
                    matchError);
                continue;
            }

            snapshots.Add(new RuleSnapshot(
                rule.Id,
                rule.Name,
                rule.ProjectId,
                rule.SourcePattern,
                rule.EventTypePattern,
                conditions,
                rule.Priority,
                rule.AgentId,
                rule.SkillId,
                rule.ModelProfileId,
                dispatch.ResolvedDispatchKey,
                dispatch.ResolvedPriority,
                rule.MaxAttempts,
                rule.TimeoutSeconds,
                rule.Version,
                rule.CreatedAtUtc));
        }

        return snapshots;
    }

    public async Task<IReadOnlyList<string>> LoadTaskKeysAsync(Guid eventId, CancellationToken cancellationToken)
        => await dbContext.Tasks
            .AsNoTracking()
            .Where(task => task.EventId == eventId)
            .Select(task => task.LogicalTaskKey)
            .ToListAsync(cancellationToken);

    public async Task<bool> SaveAsync(
        Guid eventId,
        EventStatus status,
        DateTime processedAtUtc,
        IReadOnlyList<AutomationTask> tasks,
        CancellationToken cancellationToken)
    {
        var evt = await dbContext.Events.FirstOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken)
                  ?? throw new InvalidOperationException($"Event '{eventId}' disappeared during dispatch.");

        evt.Status = status;
        evt.ProcessedAtUtc = processedAtUtc;

        if (tasks.Count > 0)
        {
            dbContext.Tasks.AddRange(tasks);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (SqlErrorHelper.IsUniqueKeyViolation(exception))
        {
            // Another dispatcher created the same logical task first; clear the failed unit of
            // work so the caller can retry against the now-committed rows.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
