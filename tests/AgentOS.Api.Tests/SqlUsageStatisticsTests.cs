using AgentOS.Api.Usage;
using AgentOS.Domain.Agents;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using AgentOS.Domain.Usage;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlUsageStatisticsTests
{
    [SkippableFact]
    public async Task Aggregates_tokens_and_estimates_cost()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var project = new Project { Id = Guid.CreateVersion7(), Name = "Usage", Slug = $"usage-{suffix}", IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var agent = new Agent { Id = Guid.CreateVersion7(), ProjectId = project.Id, Code = $"usage-agent-{suffix}", DisplayName = "Agent", Role = "coder", PermissionProfile = "workspace-write", IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var task = new AutomationTask
        {
            Id = Guid.CreateVersion7(),
            TaskCode = $"USAGE-{suffix}".ToUpperInvariant(),
            ProjectId = project.Id,
            AgentId = agent.Id,
            LogicalTaskKey = $"usage-{suffix}",
            Status = TaskStatus.Succeeded,
            Priority = "normal",
            Title = "usage",
            InputJson = "{}",
            MaxAttempts = 1,
            TimeoutSeconds = 60,
            NextRunAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var worker = new Worker { Id = Guid.CreateVersion7(), WorkerCode = $"usage-worker-{suffix}", DisplayName = "Worker", HostName = "localhost", Platform = "Linux", Architecture = "x64", Version = "1.0.0", Status = WorkerStatus.Online, LastHeartbeatAtUtc = DateTime.UtcNow, RegisteredAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var attempt = new TaskAttempt { Id = Guid.CreateVersion7(), TaskId = task.Id, WorkerId = worker.Id, AttemptNumber = 1, Status = AttemptStatus.Succeeded, StartedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        context.Projects.Add(project);
        context.Agents.Add(agent);
        context.Workers.Add(worker);
        context.Tasks.Add(task);
        context.TaskAttempts.Add(attempt);
        context.UsageRecords.Add(new UsageRecord
        {
            Id = Guid.CreateVersion7(), TaskId = task.Id, AttemptId = attempt.Id,
            Provider = "codex", Model = "gpt-test", InputTokens = 1_000_000, OutputTokens = 500_000,
            TotalTokens = 1_500_000, Source = "adapter", CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = new UsageStatisticsService(context, Options.Create(new UsageOptions
        {
            Pricing = new Dictionary<string, UsagePricingOptions>
            {
                ["codex|gpt-test"] = new() { InputPerMillionTokens = 2m, OutputPerMillionTokens = 4m }
            }
        }));
        var result = await service.QueryAsync(project.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.CallCount);
        Assert.Equal(1_500_000, item.TotalTokens);
        Assert.Equal(4m, item.EstimatedCost); // 1M*2 + 0.5M*4 = 4
    }
}
