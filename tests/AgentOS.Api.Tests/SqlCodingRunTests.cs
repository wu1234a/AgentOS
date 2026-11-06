using AgentOS.Application.CodingRuns;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Agents;
using AgentOS.Domain.CodingRuns;
using AgentOS.Domain.Planning;
using AgentOS.Domain.Projects;
using AgentOS.Infrastructure.CodingRuns;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

/// <summary>AOS-013 acceptance tests for Coding Run orchestration.</summary>
[Collection("SqlServer")]
public sealed class SqlCodingRunTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 14, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Approved_draft_creates_queued_run_and_task()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var (draft, project, agent) = await SeedAsync(context, PlanningDraftStatus.Approved);
        var service = CreateService(context);

        var result = await service.CreateAsync(
            new CreateCodingRunRequest
            {
                DraftId = draft.Id,
                ProjectId = project.Id,
                AgentId = agent.Id,
                ValidationCommands = ["dotnet test"]
            },
            CancellationToken.None);

        Assert.Equal(CodingRunOutcome.Ok, result.Outcome);
        Assert.Equal("Queued", result.Run!.Status);
        Assert.NotNull(result.Run.TaskId);
        Assert.Equal(64, result.Run.DraftContentHash.Length);

        var task = await context.Tasks.SingleAsync(candidate => candidate.Id == result.Run.TaskId);
        Assert.Equal(TaskStatus.Queued, task.Status);
        Assert.Null(task.ModelProfileId);
        Assert.Contains(draft.Id.ToString(), task.InputJson);
        Assert.Contains(result.Run.Id.ToString(), task.InputJson);
        Assert.Contains("\"validationCommands\"", task.InputJson);
        Assert.Contains("dotnet test", task.InputJson);

        var detail = await service.GetAsync(result.Run.Id, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal("created", detail!.Events[0].EventType);

        var list = await service.ListAsync(null, 10, 0, CancellationToken.None);
        Assert.Contains(list.Items, item => item.Id == result.Run.Id);

        var duplicate = await service.CreateAsync(
            new CreateCodingRunRequest { DraftId = draft.Id, ProjectId = project.Id, AgentId = agent.Id },
            CancellationToken.None);
        Assert.Equal(CodingRunOutcome.Conflict, duplicate.Outcome);
    }

    [SkippableFact]
    public async Task Project_mismatch_is_rejected()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var (draft, _, _) = await SeedAsync(context, PlanningDraftStatus.Approved);

        var otherProject = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Other",
            Slug = $"other-{Guid.NewGuid():N}"[..16],
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Projects.Add(otherProject);
        var otherAgent = new Agent
        {
            Id = Guid.CreateVersion7(),
            ProjectId = otherProject.Id,
            Code = $"coder-{Guid.NewGuid():N}"[..14],
            DisplayName = "Other Coder",
            Role = "coder",
            PermissionProfile = "workspace-write",
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Agents.Add(otherAgent);
        await context.SaveChangesAsync();

        var result = await CreateService(context).CreateAsync(
            new CreateCodingRunRequest
            {
                DraftId = draft.Id,
                ProjectId = otherProject.Id,
                AgentId = otherAgent.Id
            },
            CancellationToken.None);

        Assert.Equal(CodingRunOutcome.ValidationFailed, result.Outcome);
        Assert.Equal("project-mismatch", result.ErrorCode);
    }

    [SkippableFact]
    public async Task Unapproved_draft_is_rejected()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var (draft, project, agent) = await SeedAsync(context, PlanningDraftStatus.PendingReview);
        var service = CreateService(context);

        var result = await service.CreateAsync(
            new CreateCodingRunRequest { DraftId = draft.Id, ProjectId = project.Id, AgentId = agent.Id },
            CancellationToken.None);

        Assert.Equal(CodingRunOutcome.ValidationFailed, result.Outcome);
        Assert.Equal("draft-not-approved", result.ErrorCode);
    }

    [SkippableFact]
    public async Task Cancel_moves_queued_run_and_task_to_canceled()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var (draft, project, agent) = await SeedAsync(context, PlanningDraftStatus.Approved);
        var service = CreateService(context);

        var created = await service.CreateAsync(
            new CreateCodingRunRequest { DraftId = draft.Id, ProjectId = project.Id, AgentId = agent.Id },
            CancellationToken.None);

        var canceled = await service.CancelAsync(created.Run!.Id, CancellationToken.None);

        Assert.Equal(CodingRunOutcome.Ok, canceled.Outcome);
        Assert.Equal("Canceled", canceled.Run!.Status);
        var task = await context.Tasks.SingleAsync(candidate => candidate.Id == created.Run.TaskId);
        Assert.Equal(TaskStatus.Cancelled, task.Status);
    }

    [SkippableFact]
    public async Task Terminal_task_projects_to_run_and_retry_creates_a_new_task()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var (draft, project, agent) = await SeedAsync(context, PlanningDraftStatus.Approved);
        var service = CreateService(context);

        var created = await service.CreateAsync(
            new CreateCodingRunRequest { DraftId = draft.Id, ProjectId = project.Id, AgentId = agent.Id },
            CancellationToken.None);

        var task = await context.Tasks.SingleAsync(candidate => candidate.Id == created.Run!.TaskId);
        task.Status = TaskStatus.Failed;
        task.ErrorCode = "validation-failed";
        task.ErrorMessage = "tests failed";
        task.CompletedAtUtc = Now;
        await context.SaveChangesAsync();

        var failed = await service.GetAsync(created.Run.Id, CancellationToken.None);
        Assert.Equal("Failed", failed!.Run.Status);
        Assert.Equal("validation-failed", failed.Run.ErrorCode);

        var retried = await service.RetryAsync(created.Run.Id, CancellationToken.None);
        Assert.Equal(CodingRunOutcome.Ok, retried.Outcome);
        Assert.Equal("Queued", retried.Run!.Status);
        Assert.Equal(2, retried.Run.AttemptCount);
        Assert.NotEqual(created.Run.TaskId, retried.Run.TaskId);

        var tasks = await context.Tasks
            .Where(candidate => candidate.LogicalTaskKey.StartsWith($"coding-run:{created.Run.Id}"))
            .ToListAsync();
        Assert.Equal(2, tasks.Count);
    }

    private static CodingRunService CreateService(AgentOSDbContext context)
        => new(
            new CodingRunRepository(context),
            new TaskQueueService(
                new TaskQueueStore(context),
                new TaskLogStore(context),
                TimeProvider.System,
                Options.Create(new TaskQueueOptions()),
                NullLogger<TaskQueueService>.Instance),
            TimeProvider.System,
            NullLogger<CodingRunService>.Instance);

    private static async Task<(PlanningDraft Draft, Project Project, Agent Agent)> SeedAsync(
        AgentOSDbContext context,
        PlanningDraftStatus status)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Coding",
            Slug = $"coding-{suffix}",
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Projects.Add(project);

        var agent = new Agent
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Code = $"coder-{suffix}",
            DisplayName = "Coder",
            Role = "coder",
            PermissionProfile = "workspace-write",
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Agents.Add(agent);

        var draft = new PlanningDraft
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Title = $"AOS-099 {suffix}",
            Goal = "Implement the approved task.",
            Status = status,
            ContentMarkdown = "# AOS-099\n## 1. 目标\nImplement it.\n## 5. 验收\n- [ ] works",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.PlanningDrafts.Add(draft);
        await context.SaveChangesAsync();

        return (draft, project, agent);
    }

    private static async Task MigrateAsync(string connectionString)
    {
        await using var context = NewContext(connectionString);
        await context.Database.MigrateAsync();
    }

    private static AgentOSDbContext NewContext(string connectionString)
        => new(new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);

    private static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION to a disposable database.");
        return connectionString!;
    }
}
