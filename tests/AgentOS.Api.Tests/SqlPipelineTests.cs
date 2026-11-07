using AgentOS.Api.Pipelines;
using AgentOS.Domain.Agents;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlPipelineTests
{
    [SkippableFact]
    public async Task Returns_task_pipeline()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var project = new Project { Id = Guid.CreateVersion7(), Name = "Pipe", Slug = $"pipe-{suffix}", IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var agent = new Agent { Id = Guid.CreateVersion7(), ProjectId = project.Id, Code = $"pipe-agent-{suffix}", DisplayName = "Agent", Role = "coder", PermissionProfile = "workspace-write", IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var task = new AutomationTask
        {
            Id = Guid.CreateVersion7(), TaskCode = $"PIPE-{suffix}".ToUpperInvariant(), ProjectId = project.Id, AgentId = agent.Id,
            LogicalTaskKey = $"pipe-{suffix}", Status = TaskStatus.Queued, Priority = "normal", Title = "pipe",
            InputJson = "{}", MaxAttempts = 1, TimeoutSeconds = 60, NextRunAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        context.Projects.Add(project);
        context.Agents.Add(agent);
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        var service = new PipelineService(context);
        var view = await service.GetAsync(task.Id, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal(task.TaskCode, view!.TaskCode);
        Assert.Contains(view.Stages, stage => stage.Name == "task");
    }
}
