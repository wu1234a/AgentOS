using AgentOS.Application.Tasks;
using AgentOS.Domain.Agents;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlSubtaskTests
{
    [SkippableFact]
    public async Task Creates_blocked_subtask_and_summary()
    {
        var connectionString = RequireConnectionString();
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var project = new Project { Id = Guid.CreateVersion7(), Name = "Sub", Slug = $"sub-{suffix}", IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var agent = new Agent { Id = Guid.CreateVersion7(), ProjectId = project.Id, Code = $"coder-{suffix}", DisplayName = "Coder", Role = "coder", PermissionProfile = "workspace-write", IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var parent = NewTask(project, agent, suffix, "parent", TaskStatus.Queued);
        var prerequisite = NewTask(project, agent, suffix, "prereq", TaskStatus.Queued);
        context.Projects.Add(project);
        context.Agents.Add(agent);
        context.Tasks.AddRange(parent, prerequisite);
        await context.SaveChangesAsync();

        var service = new SubtaskService(new SubtaskRepository(context), TimeProvider.System, NullLogger<SubtaskService>.Instance);
        var created = await service.CreateAsync(parent.Id, new CreateSubtaskRequest
        {
            Title = "blocked child",
            PhaseKey = "coding",
            DependsOnTaskIds = [prerequisite.Id]
        }, CancellationToken.None);

        Assert.Equal(SubtaskOutcome.Ok, created.Outcome);
        Assert.Equal("Blocked", created.Subtask!.Status);
        Assert.Equal("coding", created.Subtask.PhaseKey);

        var list = await service.ListAsync(parent.Id, CancellationToken.None);
        Assert.NotNull(list);
        Assert.Single(list!.Items);
        Assert.Equal(1, list.Summary.Blocked);

        var duplicate = await service.CreateAsync(parent.Id, new CreateSubtaskRequest
        {
            Title = "blocked child",
            PhaseKey = "coding"
        }, CancellationToken.None);
        Assert.Equal(SubtaskOutcome.Conflict, duplicate.Outcome);
    }

    private static AutomationTask NewTask(Project project, Agent agent, string suffix, string name, TaskStatus status)
        => new()
        {
            Id = Guid.CreateVersion7(),
            TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
            ProjectId = project.Id,
            AgentId = agent.Id,
            LogicalTaskKey = $"subtask-{suffix}-{name}-{Guid.NewGuid():N}",
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
