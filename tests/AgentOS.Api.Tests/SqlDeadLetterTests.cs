using AgentOS.Api.Operations;
using AgentOS.Domain.Agents;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlDeadLetterTests
{
    [SkippableFact]
    public async Task Lists_and_requeues_failed_task()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var project = new Project { Id = Guid.CreateVersion7(), Name = "Dead", Slug = $"dead-{suffix}", IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var agent = new Agent { Id = Guid.CreateVersion7(), ProjectId = project.Id, Code = $"dead-agent-{suffix}", DisplayName = "Agent", Role = "coder", PermissionProfile = "workspace-write", IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var task = new AutomationTask
        {
            Id = Guid.CreateVersion7(), TaskCode = $"DEAD-{suffix}".ToUpperInvariant(), ProjectId = project.Id, AgentId = agent.Id,
            LogicalTaskKey = $"dead-{suffix}", Status = TaskStatus.Failed, Priority = "normal", Title = "dead",
            InputJson = "{}", MaxAttempts = 2, TimeoutSeconds = 60, NextRunAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, ErrorCode = "boom"
        };
        context.Projects.Add(project);
        context.Agents.Add(agent);
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        var service = new DeadLetterService(context, TimeProvider.System, NullLogger<DeadLetterService>.Instance);
        var items = await service.ListAsync(project.Id, "Failed", 10, CancellationToken.None);
        Assert.Contains(items, item => item.Id == task.Id);

        Assert.True(await service.RetryAsync(task.Id, CancellationToken.None));
        await context.Entry(task).ReloadAsync();
        Assert.Equal(TaskStatus.Queued, task.Status);
        Assert.Equal(0, task.AttemptCount);
    }
}
