using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Rules;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Events;
using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

public sealed class TaskDispatchServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("0199c0de-0000-7000-8000-0000000000f1");
    private static readonly Guid AgentId = Guid.Parse("0199c0de-0000-7000-8000-0000000000a1");
    private static readonly DateTime Now = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);

    private readonly FakeTaskDispatchStore _store = new();
    private readonly TaskDispatchService _service;

    public TaskDispatchServiceTests()
    {
        _service = new TaskDispatchService(
            _store,
            new ManualTimeProvider(Now),
            NullLogger<TaskDispatchService>.Instance);
    }

    [Fact]
    public async Task Matching_rule_creates_a_queued_task_with_rule_traceability()
    {
        var rule = Rule(version: 3);
        _store.Rules.Add(rule);

        var result = await _service.DispatchAsync(Event(), CancellationToken.None);

        Assert.Equal(new DispatchResult(1, 1, EventStatus.Processed), result);
        var task = Assert.Single(_store.SavedTasks);
        Assert.Equal(ProjectId, task.ProjectId);
        Assert.Equal(rule.Id, task.RuleId);
        Assert.Equal(3, task.RuleVersion);
        Assert.Equal(TaskStatus.Queued, task.Status);
        Assert.Equal(LogicalTaskKey.Create(EventId, rule.Id, 3, "default"), task.LogicalTaskKey);
        Assert.StartsWith("TASK-", task.TaskCode);
        Assert.Equal(EventStatus.Processed, _store.EventStatuses[EventId]);
    }

    [Fact]
    public async Task No_matching_rule_marks_the_event_ignored_and_keeps_it()
    {
        _store.Rules.Add(Rule(eventTypePattern: "other.event.created"));

        var result = await _service.DispatchAsync(Event(), CancellationToken.None);

        Assert.Equal(0, result.MatchedRuleCount);
        Assert.Empty(_store.SavedTasks);
        Assert.Equal(EventStatus.Ignored, result.Status);
        Assert.Equal(EventStatus.Ignored, _store.EventStatuses[EventId]);
    }

    [Fact]
    public async Task Multiple_matching_rules_create_one_task_each_in_rule_order()
    {
        var high = Rule(name: "high", priority: 10);
        var low = Rule(name: "low", priority: 1);
        _store.Rules.Add(low);
        _store.Rules.Add(high);

        var result = await _service.DispatchAsync(Event(), CancellationToken.None);

        Assert.Equal(2, result.CreatedTaskCount);
        Assert.Equal([high.Id, low.Id], _store.SavedTasks.Select(task => task.RuleId));
        Assert.Equal(2, _store.SavedTasks.Select(task => task.TaskCode).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Generated_title_is_truncated_to_the_persisted_limit()
    {
        var longEventType = "development." + new string('a', 106) + ".created"; // 120 chars
        var longRuleName = new string('r', 120);
        _store.Rules.Add(Rule(name: longRuleName, eventTypePattern: "*"));

        await _service.DispatchAsync(Event(eventType: longEventType), CancellationToken.None);

        var task = Assert.Single(_store.SavedTasks);
        Assert.Equal(200, task.Title.Length);
    }

    [Fact]
    public async Task Task_codes_stay_unique_within_one_dispatch_batch()
    {
        _store.Rules.Add(Rule(name: "one", priority: 2));
        _store.Rules.Add(Rule(name: "two", priority: 1));

        await _service.DispatchAsync(Event(), CancellationToken.None);

        Assert.Equal(2, _store.SavedTasks.Count);
        Assert.Equal(
            2,
            _store.SavedTasks.Select(task => task.TaskCode).Distinct(StringComparer.Ordinal).Count());
        Assert.All(_store.SavedTasks, task => Assert.True(task.TaskCode.Length <= 40));
    }

    [Fact]
    public async Task Redispatch_does_not_create_duplicate_logical_tasks()
    {
        _store.Rules.Add(Rule());
        await _service.DispatchAsync(Event(), CancellationToken.None);

        var second = await _service.DispatchAsync(Event(), CancellationToken.None);

        Assert.Equal(0, second.CreatedTaskCount);
        Assert.Equal(1, second.MatchedRuleCount);
        Assert.Single(_store.SavedTasks);
        Assert.Equal(EventStatus.Processed, _store.EventStatuses[EventId]);
    }

    [Fact]
    public async Task Concurrent_logical_key_conflict_is_retried_once()
    {
        _store.Rules.Add(Rule());
        _store.ConflictsToReport = 1;

        var result = await _service.DispatchAsync(Event(), CancellationToken.None);

        Assert.Equal(1, result.CreatedTaskCount);
        Assert.Equal(2, _store.SaveCalls);
        Assert.Single(_store.SavedTasks);
    }

    [Fact]
    public async Task Project_less_event_with_global_rule_is_ignored_without_tasks()
    {
        _store.Rules.Add(Rule(globalProject: true));

        var result = await _service.DispatchAsync(Event(noProject: true), CancellationToken.None);

        Assert.Equal(1, result.MatchedRuleCount);
        Assert.Equal(0, result.CreatedTaskCount);
        Assert.Equal(EventStatus.Ignored, result.Status);
        Assert.Empty(_store.SavedTasks);
    }

    [Fact]
    public async Task Task_priority_and_limits_come_from_the_rule()
    {
        _store.Rules.Add(Rule(taskPriority: "urgent", maxAttempts: 5, timeoutSeconds: 600));

        await _service.DispatchAsync(Event(), CancellationToken.None);

        var task = Assert.Single(_store.SavedTasks);
        Assert.Equal("urgent", task.Priority);
        Assert.Equal(5, task.MaxAttempts);
        Assert.Equal(600, task.TimeoutSeconds);
        Assert.Equal(Now, task.NextRunAtUtc);
    }

    private static readonly Guid EventId = Guid.Parse("0199c0de-0000-7000-8000-0000000000e1");

    private static Event Event(
        Guid? projectId = null,
        bool noProject = false,
        string eventType = "development.task.created") => new()
    {
        Id = EventId,
        ProjectId = noProject ? null : projectId ?? ProjectId,
        Source = "3derp",
        EventType = eventType,
        SchemaVersion = "1.0",
        DedupeKey = "3derp:DEV-1",
        Summary = "DEV-1 created",
        PayloadJson = """{"description":"x"}""",
        SubjectType = "development_task",
        Priority = "normal",
        Status = EventStatus.Received,
        OccurredAtUtc = Now,
        ReceivedAtUtc = Now
    };

    private static RuleSnapshot Rule(
        string name = "3derp-dev-plan",
        Guid? projectId = null,
        bool globalProject = false,
        string sourcePattern = "3derp",
        string eventTypePattern = "development.task.*",
        int priority = 10,
        int version = 1,
        string dispatchKey = "default",
        string taskPriority = "normal",
        int maxAttempts = 2,
        int timeoutSeconds = 1800)
        => new(
            Guid.CreateVersion7(),
            name,
            globalProject ? null : projectId ?? ProjectId,
            sourcePattern,
            eventTypePattern,
            new RuleMatchConditions(),
            priority,
            AgentId,
            null,
            null,
            dispatchKey,
            taskPriority,
            maxAttempts,
            timeoutSeconds,
            version,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
}
