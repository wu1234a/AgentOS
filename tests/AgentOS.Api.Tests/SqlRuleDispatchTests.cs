using System.Text.Json;
using AgentOS.Application.Events;
using AgentOS.Application.Rules;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Agents;
using AgentOS.Domain.Events;
using AgentOS.Domain.ModelProfiles;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Rules;
using AgentOS.Domain.Skills;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Projects;
using AgentOS.Infrastructure.Repositories;
using AgentOS.Infrastructure.Rules;
using AgentOS.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

/// <summary>AOS-004 acceptance tests against a real SQL Server.</summary>
[Collection("SqlServer")]
public sealed class SqlRuleDispatchTests
{
    [SkippableFact]
    public async Task Dispatch_creates_one_task_per_rule_and_keeps_retries_idempotent()
    {
        var connectionString = RequireConnectionString();
        await using var dbContext = new AgentOSDbContext(Options(connectionString));
        await dbContext.Database.MigrateAsync();

        var (project, agent) = await SeedReferencesAsync(dbContext);
        var rule = NewRule(project.Id, agent.Id, priority: 10, version: 4);
        dbContext.AutomationRules.Add(rule);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var dedupeKey = $"dispatch-{Guid.NewGuid():N}";

        var created = await service.CreateAsync(Request(project.Slug, dedupeKey), CancellationToken.None);

        Assert.Equal(CreateEventOutcome.Created, created.Outcome);
        Assert.Equal(EventStatus.Processed, created.Status);
        Assert.Equal(1, created.TasksCreated);

        var tasks = await dbContext.Tasks
            .Where(task => task.EventId == created.EventId)
            .ToListAsync();
        var task = Assert.Single(tasks);
        Assert.Equal(rule.Id, task.RuleId);
        Assert.Equal(4, task.RuleVersion);
        Assert.Equal(TaskStatus.Queued, task.Status);
        Assert.Equal(LogicalTaskKey.Create(created.EventId, rule.Id, 4, "default"), task.LogicalTaskKey);
        Assert.Equal(project.Id, task.ProjectId);

        // Redelivery of the same event must not create a second logical task.
        var duplicate = await service.CreateAsync(Request(project.Slug, dedupeKey), CancellationToken.None);

        Assert.Equal(CreateEventOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(created.EventId, duplicate.EventId);
        Assert.Equal(0, duplicate.TasksCreated);
        Assert.Single(await dbContext.Tasks.Where(candidate => candidate.EventId == created.EventId).ToListAsync());
    }

    [SkippableFact]
    public async Task Dispatch_skips_rules_whose_agent_is_disabled()
    {
        var connectionString = RequireConnectionString();
        await using var dbContext = new AgentOSDbContext(Options(connectionString));
        await dbContext.Database.MigrateAsync();

        var (project, agent) = await SeedReferencesAsync(dbContext, agentEnabled: false);
        dbContext.AutomationRules.Add(NewRule(project.Id, agent.Id));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var result = await service.CreateAsync(
            Request(project.Slug, $"disabled-{Guid.NewGuid():N}"),
            CancellationToken.None);

        Assert.Equal(EventStatus.Ignored, result.Status);
        Assert.Equal(0, result.TasksCreated);
        Assert.Empty(await dbContext.Tasks.Where(task => task.EventId == result.EventId).ToListAsync());
    }

    [SkippableFact]
    public async Task Logical_task_key_unique_index_rejects_duplicate_tasks()
    {
        var connectionString = RequireConnectionString();
        await using var dbContext = new AgentOSDbContext(Options(connectionString));
        await dbContext.Database.MigrateAsync();

        var (project, agent) = await SeedReferencesAsync(dbContext);
        var rule = NewRule(project.Id, agent.Id);

        var eventId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        dbContext.Events.Add(new Event
        {
            Id = eventId,
            ProjectId = project.Id,
            Source = "3derp",
            EventType = "development.task.created",
            SchemaVersion = "1.0",
            DedupeKey = $"unique-key-{Guid.NewGuid():N}",
            Summary = "seed event",
            PayloadJson = "{}",
            Priority = "normal",
            Status = EventStatus.Received,
            OccurredAtUtc = now,
            ReceivedAtUtc = now
        });
        dbContext.AutomationRules.Add(rule);
        await dbContext.SaveChangesAsync();

        var logicalKey = LogicalTaskKey.Create(eventId, rule.Id, 1, "default");

        dbContext.Tasks.Add(NewTask(eventId, project.Id, rule.Id, agent.Id, logicalKey, now));
        await dbContext.SaveChangesAsync();

        dbContext.Tasks.Add(NewTask(eventId, project.Id, rule.Id, agent.Id, logicalKey, now));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        Assert.True(SqlErrorHelper.IsUniqueKeyViolation(exception));
    }

    [SkippableFact]
    public async Task Rule_service_round_trips_through_the_real_repository()
    {
        var connectionString = RequireConnectionString();
        await using var dbContext = new AgentOSDbContext(Options(connectionString));
        await dbContext.Database.MigrateAsync();

        var (project, agent) = await SeedReferencesAsync(dbContext);
        var service = new RuleService(
            new RuleValidator(new RuleReferenceLookup(dbContext)),
            new AutomationRuleRepository(dbContext),
            TimeProvider.System);

        var created = await service.CreateAsync(
            SaveRequest(project.Slug, agent.Code, priority: 7, isEnabled: true),
            CancellationToken.None);

        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.SelectMany(pair => pair.Value)));
        var detail = created.Rule!;
        Assert.Equal(project.Slug, detail.ProjectSlug);
        Assert.Equal(agent.Code, detail.AgentCode);
        Assert.Equal(1, detail.Version);

        var loaded = await service.GetAsync(detail.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("3derp", loaded!.SourcePattern);

        var page = await service.ListAsync(new RuleListQuery(1, 10, null, project.Slug), CancellationToken.None);
        Assert.Equal(1, page.Total);

        // PUT without isEnabled keeps the rule enabled rather than silently disabling it.
        var updated = await service.UpdateAsync(
            detail.Id,
            SaveRequest(project.Slug, agent.Code, priority: 9, isEnabled: null),
            CancellationToken.None);

        Assert.True(updated!.IsSuccess);
        Assert.Equal(2, updated.Rule!.Version);
        Assert.Equal(9, updated.Rule.Priority);
        Assert.True(updated.Rule.IsEnabled);
    }

    private static SaveRuleRequest SaveRequest(
        string projectSlug,
        string agentCode,
        int priority,
        bool? isEnabled) => new()
    {
        Name = $"rule-{Guid.NewGuid():N}",
        Project = projectSlug,
        SourcePattern = "3derp",
        EventTypePattern = "development.task.*",
        AgentCode = agentCode,
        MatchJson = JsonSerializer.Deserialize<JsonElement>("""{"subjectTypes":["development_task"]}"""),
        DispatchJson = JsonSerializer.Deserialize<JsonElement>("""{"dispatchKey":"default","priority":"normal"}"""),
        Priority = priority,
        MaxParallel = 1,
        MaxAttempts = 2,
        TimeoutSeconds = 1800,
        NotificationPolicy = "none",
        IsEnabled = isEnabled
    };

    private static EventService CreateService(AgentOSDbContext dbContext) => new(
        new CreateEventValidator(),
        new EventPayloadHasher(),
        new DuplicateEventLogSampler(TimeProvider.System),
        new EventRepository(dbContext),
        new ProjectLookup(dbContext),
        new TaskDispatchService(
            new TaskDispatchStore(dbContext, NullLogger<TaskDispatchStore>.Instance),
            TimeProvider.System,
            NullLogger<TaskDispatchService>.Instance),
        TimeProvider.System,
        NullLogger<EventService>.Instance);

    private static async Task<(Project Project, Agent Agent)> SeedReferencesAsync(
        AgentOSDbContext dbContext,
        bool agentEnabled = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Rules Integration",
            Slug = $"rules-{suffix}",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var profile = new ModelProfile
        {
            Id = Guid.CreateVersion7(),
            Code = $"profile-{suffix}",
            Provider = "codex-cli",
            Model = "gpt-test",
            ExecutionMode = "cli",
            ConfigurationJson = "{}",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var skill = new Skill
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Code = $"skill-{suffix}",
            Version = "1.0",
            RelativePath = "skills/software-development/rules-integration/SKILL.md",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var agent = new Agent
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Code = $"planner-{suffix}",
            DisplayName = "Planner",
            Role = "planner",
            DefaultModelProfileId = profile.Id,
            PermissionProfile = "read-only",
            IsEnabled = agentEnabled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        dbContext.Projects.Add(project);
        dbContext.ModelProfiles.Add(profile);
        dbContext.Skills.Add(skill);
        dbContext.Agents.Add(agent);
        await dbContext.SaveChangesAsync();

        return (project, agent);
    }

    private static AutomationRule NewRule(Guid projectId, Guid agentId, int priority = 0, int version = 1) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = $"rule-{Guid.NewGuid():N}",
        ProjectId = projectId,
        SourcePattern = "3derp",
        EventTypePattern = "development.task.*",
        MatchJson = """{"subjectTypes":["development_task"]}""",
        AgentId = agentId,
        DispatchJson = """{"dispatchKey":"default","priority":"normal"}""",
        Priority = priority,
        MaxParallel = 1,
        MaxAttempts = 2,
        TimeoutSeconds = 1800,
        NotificationPolicy = "none",
        IsEnabled = true,
        Version = version,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static AutomationTask NewTask(
        Guid eventId,
        Guid projectId,
        Guid ruleId,
        Guid agentId,
        string logicalKey,
        DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        TaskCode = "TASK-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
        EventId = eventId,
        ProjectId = projectId,
        RuleId = ruleId,
        RuleVersion = 1,
        AgentId = agentId,
        LogicalTaskKey = logicalKey,
        Status = TaskStatus.Queued,
        Priority = "normal",
        Title = "test task",
        InputJson = "{}",
        MaxAttempts = 2,
        TimeoutSeconds = 1800,
        NextRunAtUtc = now,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private static CreateEventRequest Request(string projectSlug, string dedupeKey) => new()
    {
        SchemaVersion = "1.0",
        Source = "3derp",
        EventType = "development.task.created",
        Project = projectSlug,
        Summary = "rules dispatch integration",
        DedupeKey = dedupeKey,
        OccurredAtUtc = "2026-11-06T00:00:00Z",
        Subject = new EventSubjectRequest { Type = "development_task", Id = "DEV-1" },
        Payload = JsonSerializer.Deserialize<JsonElement>("""{"description":"x"}""")
    };

    private static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            "Set AGENTOS_SQL_TEST_CONNECTION to a disposable database to run the SQL rule dispatch tests.");
        return connectionString!;
    }

    private static DbContextOptions<AgentOSDbContext> Options(string connectionString)
        => new DbContextOptionsBuilder<AgentOSDbContext>()
            .UseSqlServer(connectionString)
            .Options;
}
