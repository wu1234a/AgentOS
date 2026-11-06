using AgentOS.Application.Rules;
using AgentOS.Domain.Events;

namespace AgentOS.Api.Tests;

public sealed class RuleMatcherTests
{
    private static readonly Guid ProjectA = Guid.Parse("0199c0de-0000-7000-8000-00000000000a");
    private static readonly Guid ProjectB = Guid.Parse("0199c0de-0000-7000-8000-00000000000b");
    private static readonly Guid AgentId = Guid.Parse("0199c0de-0000-7000-8000-0000000000aa");

    [Fact]
    public void Exact_patterns_match_source_and_event_type()
    {
        var rule = Snapshot("3derp", "development.task.created");
        var evt = Event(source: "3derp", eventType: "development.task.created");

        Assert.True(RuleMatcher.IsMatch(rule, evt));
        Assert.False(RuleMatcher.IsMatch(rule, Event(source: "github", eventType: "development.task.created")));
        Assert.False(RuleMatcher.IsMatch(rule, Event(source: "3derp", eventType: "development.task.updated")));
    }

    [Fact]
    public void Wildcards_match_whole_value_only()
    {
        var rule = Snapshot("github.*", "*.task.created");

        Assert.True(RuleMatcher.IsMatch(rule, Event(source: "github.com", eventType: "development.task.created")));
        Assert.False(RuleMatcher.IsMatch(rule, Event(source: "github-enterprise", eventType: "x.task.created")));
    }

    [Fact]
    public void Project_scoped_rule_matches_only_its_project()
    {
        var rule = Snapshot(projectId: ProjectA);

        Assert.True(RuleMatcher.IsMatch(rule, Event(projectId: ProjectA)));
        Assert.False(RuleMatcher.IsMatch(rule, Event(projectId: ProjectB)));
        Assert.False(RuleMatcher.IsMatch(rule, Event(projectId: null)));
    }

    [Fact]
    public void Global_rule_matches_every_project()
    {
        var rule = Snapshot(projectId: null);

        Assert.True(RuleMatcher.IsMatch(rule, Event(projectId: ProjectA)));
        Assert.True(RuleMatcher.IsMatch(rule, Event(projectId: null)));
    }

    [Fact]
    public void Structured_conditions_filter_subject_and_priority()
    {
        var rule = Snapshot(
            subjectTypes: ["development_task"],
            priorities: ["high", "urgent"]);

        Assert.True(RuleMatcher.IsMatch(rule, Event(subjectType: "development_task", priority: "high")));
        Assert.False(RuleMatcher.IsMatch(rule, Event(subjectType: "pull_request", priority: "high")));
        Assert.False(RuleMatcher.IsMatch(rule, Event(subjectType: "development_task", priority: "normal")));
    }

    [Fact]
    public void Matching_order_is_project_first_then_priority_then_age_then_id()
    {
        var older = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = older.AddDays(1);

        var global = Snapshot(projectId: null, priority: 100, createdAt: older, id: Guid.Parse("0199c0de-0000-7000-8000-000000000001"));
        var projectHigh = Snapshot(projectId: ProjectA, priority: 5, createdAt: newer, id: Guid.Parse("0199c0de-0000-7000-8000-000000000002"));
        var projectLow = Snapshot(projectId: ProjectA, priority: 1, createdAt: older, id: Guid.Parse("0199c0de-0000-7000-8000-000000000003"));
        var projectHighOlder = Snapshot(projectId: ProjectA, priority: 5, createdAt: older, id: Guid.Parse("0199c0de-0000-7000-8000-000000000004"));

        var ordered = RuleMatcher.Order([global, projectHigh, projectLow, projectHighOlder]);

        Assert.Equal(
            [projectHighOlder.Id, projectHigh.Id, projectLow.Id, global.Id],
            ordered.Select(rule => rule.Id));
    }

    [Fact]
    public void Ordering_is_stable_when_input_is_shuffled()
    {
        var rules = Enumerable.Range(0, 6)
            .Select(index => Snapshot(
                projectId: index % 2 == 0 ? ProjectA : null,
                priority: index % 3,
                createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index),
                id: Guid.Parse($"0199c0de-0000-7000-8000-00000000000{index}")))
            .ToArray();

        var expected = RuleMatcher.Order(rules).Select(rule => rule.Id).ToArray();

        var shuffled = rules.OrderBy(rule => rule.Name.GetHashCode(StringComparison.Ordinal)).ToArray();
        Assert.Equal(expected, RuleMatcher.Order(shuffled).Select(rule => rule.Id));
    }

    private static RuleSnapshot Snapshot(
        string sourcePattern = "*",
        string eventTypePattern = "*",
        Guid? projectId = null,
        int priority = 0,
        DateTime? createdAt = null,
        Guid? id = null,
        string[]? subjectTypes = null,
        string[]? priorities = null)
        => new(
            id ?? Guid.CreateVersion7(),
            "rule",
            projectId,
            sourcePattern,
            eventTypePattern,
            new RuleMatchConditions
            {
                SubjectTypes = subjectTypes ?? [],
                Priorities = priorities ?? []
            },
            priority,
            AgentId,
            null,
            null,
            "default",
            "normal",
            2,
            1800,
            1,
            createdAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private static Event Event(
        string source = "3derp",
        string eventType = "development.task.created",
        Guid? projectId = null,
        string? subjectType = "development_task",
        string priority = "normal")
        => new()
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            Source = source,
            EventType = eventType,
            SchemaVersion = "1.0",
            DedupeKey = $"key-{Guid.NewGuid():N}",
            Summary = "test",
            PayloadJson = "{}",
            SubjectType = subjectType,
            Priority = priority,
            Status = EventStatus.Received,
            OccurredAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ReceivedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
}
