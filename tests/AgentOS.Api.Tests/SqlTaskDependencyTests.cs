using AgentOS.Domain.Agents;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlTaskDependencyTests
{
    [SkippableFact]
    public async Task Releases_and_fails_dependents_in_order()
    {
        var connectionString = RequireConnectionString();
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Deps",
            Slug = $"deps-{suffix}",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var agent = new Agent
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Code = $"coder-{suffix}",
            DisplayName = "Coder",
            Role = "coder",
            PermissionProfile = "workspace-write",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.Projects.Add(project);
        context.Agents.Add(agent);
        await context.SaveChangesAsync();

        var first = NewTask(project, agent, suffix, "first", TaskStatus.Succeeded);
        var second = NewTask(project, agent, suffix, "second", TaskStatus.Blocked);
        var third = NewTask(project, agent, suffix, "third", TaskStatus.Blocked);
        context.Tasks.AddRange(first, second, third);
        context.TaskDependencies.AddRange(
            new TaskDependency { TaskId = second.Id, PrerequisiteTaskId = first.Id, CreatedAtUtc = DateTime.UtcNow },
            new TaskDependency { TaskId = third.Id, PrerequisiteTaskId = second.Id, CreatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var coordinator = new TaskDependencyCoordinator(context);
        await coordinator.ReleaseAndPropagateAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(TaskStatus.Queued, (await context.Tasks.SingleAsync(task => task.Id == second.Id)).Status);
        Assert.Equal(TaskStatus.Blocked, (await context.Tasks.SingleAsync(task => task.Id == third.Id)).Status);

        second.Status = TaskStatus.Failed;
        await context.SaveChangesAsync();
        await coordinator.ReleaseAndPropagateAsync(DateTime.UtcNow, CancellationToken.None);

        var thirdAfter = await context.Tasks.SingleAsync(task => task.Id == third.Id);
        Assert.Equal(TaskStatus.Failed, thirdAfter.Status);
        Assert.Equal("dependency-failed", thirdAfter.ErrorCode);
    }

    private static AutomationTask NewTask(Project project, Agent agent, string suffix, string name, TaskStatus status)
        => new()
        {
            Id = Guid.CreateVersion7(),
            TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
            ProjectId = project.Id,
            AgentId = agent.Id,
            LogicalTaskKey = $"deps-{suffix}-{name}-{Guid.NewGuid():N}",
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

    private static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        return connectionString!;
    }
}
