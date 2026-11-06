using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

public sealed class LeaseRecoveryServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid WorkerId = Guid.Parse("0199c0de-0000-7000-8000-0000000000b2");

    private readonly FakeTaskQueueStore _store = new();
    private readonly TaskQueueOptions _options = new()
    {
        BackoffBaseSeconds = 30,
        BackoffMaxSeconds = 900,
        MaxAttempts = 2
    };

    [Fact]
    public async Task Expired_lease_is_requeued_with_backoff_and_the_attempt_is_lost()
    {
        var (task, attempt) = SeedRunning(attemptCount: 1, maxAttempts: 2, leaseExpiredSecondsAgo: 5);

        var stats = await CreateService().RecoverAsync(CancellationToken.None);

        Assert.Equal(new LeaseRecoveryStats(1, 0), stats);
        Assert.Equal(TaskStatus.RetryWait, task.Status);
        Assert.True(task.NextRunAtUtc > Now);
        Assert.Equal(AttemptStatus.Lost, attempt.Status);
        Assert.Equal("lease-expired", attempt.ErrorCode);
        Assert.Null(task.LeaseTokenHash);
        Assert.Null(task.LeaseOwnerWorkerId);
    }

    [Fact]
    public async Task Expired_lease_on_the_last_attempt_fails_the_task()
    {
        var (task, _) = SeedRunning(attemptCount: 2, maxAttempts: 2, leaseExpiredSecondsAgo: 5);

        var stats = await CreateService().RecoverAsync(CancellationToken.None);

        Assert.Equal(new LeaseRecoveryStats(0, 1), stats);
        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Equal(Now, task.CompletedAtUtc);
    }

    [Fact]
    public async Task A_live_lease_is_left_alone()
    {
        var (task, _) = SeedRunning(attemptCount: 1, maxAttempts: 2, leaseExpiredSecondsAgo: -30);

        var stats = await CreateService().RecoverAsync(CancellationToken.None);

        Assert.Equal(new LeaseRecoveryStats(0, 0), stats);
        Assert.Equal(TaskStatus.Running, task.Status);
    }

    private LeaseRecoveryService CreateService() => new(
        _store,
        new ManualTimeProvider(Now),
        Options.Create(_options),
        NullLogger<LeaseRecoveryService>.Instance);

    private (AutomationTask Task, TaskAttempt Attempt) SeedRunning(
        int attemptCount,
        int maxAttempts,
        int leaseExpiredSecondsAgo)
    {
        var task = new AutomationTask
        {
            Id = Guid.NewGuid(),
            TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
            ProjectId = Guid.Parse("0199c0de-0000-7000-8000-0000000000c2"),
            AgentId = Guid.Parse("0199c0de-0000-7000-8000-0000000000a2"),
            LogicalTaskKey = $"key-{Guid.NewGuid():N}",
            Status = TaskStatus.Running,
            Priority = "normal",
            Title = "recovery test",
            InputJson = "{}",
            AttemptCount = attemptCount,
            MaxAttempts = maxAttempts,
            NextRunAtUtc = Now,
            CreatedAtUtc = Now.AddMinutes(-10),
            UpdatedAtUtc = Now.AddMinutes(-1),
            LeaseOwnerWorkerId = WorkerId,
            LeaseTokenHash = LeaseTokenService.Hash("token"),
            LeaseExpiresAtUtc = Now.AddSeconds(-leaseExpiredSecondsAgo)
        };

        var attempt = new TaskAttempt
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            WorkerId = WorkerId,
            AttemptNumber = attemptCount,
            Status = AttemptStatus.Running,
            StartedAtUtc = Now.AddMinutes(-5),
            CreatedAtUtc = Now.AddMinutes(-5),
            UpdatedAtUtc = Now.AddMinutes(-5)
        };

        _store.Tasks.Add(task);
        _store.Attempts.Add(attempt);
        return (task, attempt);
    }
}
