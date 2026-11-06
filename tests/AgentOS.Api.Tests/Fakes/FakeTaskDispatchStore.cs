using AgentOS.Application.Rules;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Events;
using AgentOS.Domain.Tasks;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>In-memory dispatch store: lets the dispatcher be tested without EF Core.</summary>
public sealed class FakeTaskDispatchStore : ITaskDispatchStore
{
    public List<RuleSnapshot> Rules { get; } = [];

    public List<AutomationTask> SavedTasks { get; } = [];

    public Dictionary<Guid, EventStatus> EventStatuses { get; } = [];

    /// <summary>Number of upcoming saves that should report a concurrent logical-key conflict.</summary>
    public int ConflictsToReport { get; set; }

    public int SaveCalls { get; private set; }

    public List<string> ExtraExistingKeys { get; } = [];

    public Task<IReadOnlyList<RuleSnapshot>> LoadEnabledRulesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<RuleSnapshot>>(Rules);

    public Task<IReadOnlyList<string>> LoadTaskKeysAsync(Guid eventId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>(
            SavedTasks.Where(task => task.EventId == eventId)
                .Select(task => task.LogicalTaskKey)
                .Concat(ExtraExistingKeys)
                .ToArray());

    public Task<bool> SaveAsync(
        Guid eventId,
        EventStatus status,
        DateTime processedAtUtc,
        IReadOnlyList<AutomationTask> tasks,
        CancellationToken cancellationToken)
    {
        SaveCalls++;

        if (ConflictsToReport > 0)
        {
            ConflictsToReport--;
            return Task.FromResult(false);
        }

        SavedTasks.AddRange(tasks);
        EventStatuses[eventId] = status;
        return Task.FromResult(true);
    }
}
