using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Events;
using AgentOS.Application.Scheduling;
using AgentOS.Domain.Scheduling;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlScheduledJobTests
{
    [SkippableFact]
    public async Task Due_job_publishes_once_and_advances()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var now = DateTime.UtcNow;
        var job = new ScheduledJob
        {
            Id = Guid.CreateVersion7(),
            Name = "test schedule",
            EventType = "schedule.tick",
            PayloadJson = "{}",
            IntervalSeconds = 60,
            FirstRunAtUtc = now.AddMinutes(-2),
            NextRunAtUtc = now.AddMinutes(-2),
            Enabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.ScheduledJobs.Add(job);
        await context.SaveChangesAsync();

        var events = new EventService(
            new CreateEventValidator(),
            new EventPayloadHasher(),
            new DuplicateEventLogSampler(TimeProvider.System),
            new FakeEventRepository(),
            new FakeProjectLookup(),
            new FakeTaskDispatcher(),
            TimeProvider.System,
            NullLogger<EventService>.Instance);
        var service = new ScheduledJobService(
            new ScheduledJobRepository(context),
            events,
            TimeProvider.System,
            NullLogger<ScheduledJobService>.Instance);

        var triggered = await service.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, triggered);
        await context.Entry(job).ReloadAsync();
        Assert.True(job.NextRunAtUtc > DateTime.UtcNow);
        Assert.NotNull(job.LastTriggeredAtUtc);
        Assert.Equal(0, await service.RunDueAsync(CancellationToken.None));
    }
}
