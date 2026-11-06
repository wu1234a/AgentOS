using AgentOS.Application.Rules;
using AgentOS.Domain.Events;
using AgentOS.Domain.Tasks;

namespace AgentOS.Application.Tasks;

/// <summary>Persistence seam for the rules → tasks dispatch transaction.</summary>
public interface ITaskDispatchStore
{
    /// <summary>Enabled rules whose agent (and optional skill/model profile) are enabled.</summary>
    Task<IReadOnlyList<RuleSnapshot>> LoadEnabledRulesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> LoadTaskKeysAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the new tasks and the event's processing status in one transaction.
    /// Returns false when the unique logical-task key was already taken by a concurrent
    /// dispatcher, so the caller can retry the (now idempotent) pass.
    /// </summary>
    Task<bool> SaveAsync(
        Guid eventId,
        EventStatus status,
        DateTime processedAtUtc,
        IReadOnlyList<AutomationTask> tasks,
        CancellationToken cancellationToken);
}

/// <summary>Produces tasks for one persisted event.</summary>
public interface ITaskDispatcher
{
    Task<DispatchResult> DispatchAsync(Event evt, CancellationToken cancellationToken);
}

/// <summary>Outcome of one dispatch pass.</summary>
public sealed record DispatchResult(int MatchedRuleCount, int CreatedTaskCount, EventStatus Status);
