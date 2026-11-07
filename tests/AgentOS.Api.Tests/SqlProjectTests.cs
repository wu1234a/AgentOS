using AgentOS.Application.Projects;
using AgentOS.Domain.Agents;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using AgentOS.Infrastructure.ModelProfiles;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Projects;
using AgentOS.Infrastructure.Tasks;
using AgentOS.Application.Tasks;
using AgentOS.Api.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlProjectTests
{
    [SkippableFact]
    public async Task Creates_updates_and_disables_a_project()
    {
        var connectionString = Require();
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();
        var service = new ProjectService(new ProjectRepository(context), TimeProvider.System);
        var slug = $"proj-{Guid.NewGuid():N}"[..16];

        var created = await service.CreateAsync(new CreateProjectRequest
        {
            Name = "Project",
            Slug = slug,
            MaxConcurrentTasks = 2
        }, CancellationToken.None);

        Assert.Equal(ProjectOutcome.Ok, created.Outcome);
        var duplicate = await service.CreateAsync(new CreateProjectRequest { Name = "Other", Slug = slug }, CancellationToken.None);
        Assert.Equal(ProjectOutcome.Conflict, duplicate.Outcome);

        var updated = await service.UpdateAsync(created.Project!.Id, new UpdateProjectRequest { MaxConcurrentTasks = 1 }, CancellationToken.None);
        Assert.Equal(1, updated.Project!.MaxConcurrentTasks);

        var disabled = await service.SetEnabledAsync(created.Project.Id, false, CancellationToken.None);
        Assert.False(disabled.Project!.IsEnabled);
    }

    [SkippableFact]
    public async Task Quota_blocks_claim_when_concurrency_is_full()
    {
        var connectionString = Require();
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var project = new Project { Id = Guid.CreateVersion7(), Name = "Quota", Slug = $"quota-{suffix}", IsEnabled = true, MaxConcurrentTasks = 1, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var agent = new Agent { Id = Guid.CreateVersion7(), ProjectId = project.Id, Code = $"coder-{suffix}", DisplayName = "Coder", Role = "coder", PermissionProfile = "workspace-write", IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var worker = new Worker { Id = Guid.CreateVersion7(), WorkerCode = $"worker-{suffix}", DisplayName = "Runner", HostName = "localhost", Platform = "Linux", Architecture = "x64", Version = "1.0.0", Status = WorkerStatus.Online, LastHeartbeatAtUtc = DateTime.UtcNow, RegisteredAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        context.Projects.Add(project);
        context.Agents.Add(agent);
        context.Workers.Add(worker);
        context.Tasks.Add(NewTask(project, agent, suffix, "running", TaskStatus.Running));
        var queuedTask = NewTask(project, agent, suffix, "queued", TaskStatus.Queued);
        context.Tasks.Add(queuedTask);
        await context.SaveChangesAsync();

        var claimer = new TaskClaimer(context, new LeaseTokenService(), TimeProvider.System, Options.Create(new TaskQueueOptions { ClaimBatchLimit = 10 }), new FakeSkillContentReader(), new ModelProfileRepository(context), NullLogger<TaskClaimer>.Instance);
        var claimed = await claimer.ClaimAsync(worker.Id, 10, CancellationToken.None);

        Assert.DoesNotContain(claimed, task => task.TaskId == queuedTask.Id);
    }

    private static AutomationTask NewTask(Project project, Agent agent, string suffix, string name, TaskStatus status)
        => new()
        {
            Id = Guid.CreateVersion7(),
            TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
            ProjectId = project.Id,
            AgentId = agent.Id,
            LogicalTaskKey = $"quota-{suffix}-{name}-{Guid.NewGuid():N}",
            Status = status,
            Priority = "normal",
            Title = name,
            InputJson = "{}",
            MaxAttempts = 2,
            TimeoutSeconds = 600,
            NextRunAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static string Require()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        return connectionString!;
    }
}
