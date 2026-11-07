using AgentOS.Api.Workers;
using AgentOS.Domain.Workers;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlWorkerAdminTests
{
    [SkippableFact]
    public async Task Lists_and_disables_worker()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var worker = new Worker
        {
            Id = Guid.CreateVersion7(), WorkerCode = $"admin-{suffix}", DisplayName = "Runner",
            HostName = "localhost", Platform = "Linux", Architecture = "x64", Version = "1.0.0",
            Status = WorkerStatus.Online, LastHeartbeatAtUtc = DateTime.UtcNow,
            RegisteredAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        context.Workers.Add(worker);
        await context.SaveChangesAsync();

        var service = new WorkerAdminService(context, TimeProvider.System);
        Assert.Contains(await service.ListAsync(null, CancellationToken.None), item => item.Id == worker.Id);

        Assert.True(await service.SetEnabledAsync(worker.Id, false, CancellationToken.None));
        await context.Entry(worker).ReloadAsync();
        Assert.Equal(WorkerStatus.Disabled, worker.Status);
    }
}
