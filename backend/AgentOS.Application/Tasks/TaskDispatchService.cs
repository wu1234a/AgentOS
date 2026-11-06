using AgentOS.Application.Rules;
using AgentOS.Domain.Events;
using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Application.Tasks;

/// <summary>
/// Matches the persisted event against enabled rules and creates one task per rule revision
/// and dispatch key. The pass is idempotent: <c>LogicalTaskKey</c> is checked against existing
/// rows and backed by a unique index, so a retried event (or a concurrent duplicate) cannot
/// create the same logical task twice.
/// </summary>
public sealed class TaskDispatchService(
    ITaskDispatchStore store,
    TimeProvider timeProvider,
    ILogger<TaskDispatchService> logger) : ITaskDispatcher
{
    private const int MaxAttempts = 2;

    public async Task<DispatchResult> DispatchAsync(Event evt, CancellationToken cancellationToken)
    {
        var rules = await store.LoadEnabledRulesAsync(cancellationToken);
        var matched = RuleMatcher.Match(rules, evt);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var existingKeys = (await store.LoadTaskKeysAsync(evt.Id, cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var tasks = new List<AutomationTask>();
            var routedRuleCount = 0;
            foreach (var rule in matched)
            {
                var projectId = evt.ProjectId ?? rule.ProjectId;
                if (projectId is null)
                {
                    // Tasks always belong to a project; a project-less event cannot be routed.
                    logger.LogWarning(
                        "Skipping rule {RuleId} for event {EventId}: no project could be resolved.",
                        rule.Id,
                        evt.Id);
                    continue;
                }

                routedRuleCount++;

                var logicalKey = LogicalTaskKey.Create(evt.Id, rule.Id, rule.Version, rule.DispatchKey);
                if (!existingKeys.Add(logicalKey))
                {
                    continue;
                }

                tasks.Add(BuildTask(evt, rule, logicalKey, projectId.Value, now));
            }

            var status = routedRuleCount > 0 ? EventStatus.Processed : EventStatus.Ignored;

            if (await store.SaveAsync(evt.Id, status, now, tasks, cancellationToken))
            {
                if (attempt > 1)
                {
                    logger.LogInformation(
                        "Dispatch for event {EventId} succeeded on attempt {Attempt} after a concurrent writer.",
                        evt.Id,
                        attempt);
                }

                logger.LogInformation(
                    "Dispatched event {EventId}: matchedRules={MatchedRuleCount} createdTasks={CreatedTaskCount} status={Status}",
                    evt.Id,
                    matched.Count,
                    tasks.Count,
                    status);

                return new DispatchResult(matched.Count, tasks.Count, status);
            }

            logger.LogWarning(
                "Logical task key conflict while dispatching event {EventId} (attempt {Attempt}); retrying.",
                evt.Id,
                attempt);
        }

        throw new InvalidOperationException(
            $"Could not dispatch event '{evt.Id}' after {MaxAttempts} attempts because of repeated logical-key conflicts.");
    }

    private const int MaxTitleLength = 200;

    private static AutomationTask BuildTask(
        Event evt,
        RuleSnapshot rule,
        string logicalKey,
        Guid projectId,
        DateTime nowUtc)
    {
        var id = Guid.CreateVersion7();
        var title = $"{evt.EventType} · {rule.Name}";

        return new AutomationTask
        {
            Id = id,
            // The full id keeps the code unique even when several tasks are built in the same
            // millisecond; a UUIDv7 prefix alone is only a timestamp and would collide.
            TaskCode = $"TASK-{id:N}".ToUpperInvariant(),
            EventId = evt.Id,
            ProjectId = projectId,
            RuleId = rule.Id,
            RuleVersion = rule.Version,
            AgentId = rule.AgentId,
            SkillId = rule.SkillId,
            ModelProfileId = rule.ModelProfileId,
            LogicalTaskKey = logicalKey,
            Status = TaskStatus.Queued,
            Priority = rule.TaskPriority,
            Title = title.Length <= MaxTitleLength ? title : title[..MaxTitleLength],
            Description = evt.Summary,
            InputJson = evt.PayloadJson,
            MaxAttempts = rule.MaxAttempts,
            TimeoutSeconds = rule.TimeoutSeconds,
            NextRunAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }
}
