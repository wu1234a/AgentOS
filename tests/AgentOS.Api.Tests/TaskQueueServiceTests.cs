using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

public sealed class TaskQueueServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid WorkerId = Guid.Parse("0199c0de-0000-7000-8000-0000000000b1");
    private static readonly Guid OtherWorkerId = Guid.Parse("0199c0de-0000-7000-8000-0000000000a2");
    private const string Token = "lease-token-value";

    private readonly FakeTaskQueueStore _store = new();
    private readonly ManualTimeProvider _time = new(Now);
    private readonly TaskQueueOptions _options = new()
    {
        LeaseDurationSeconds = 90,
        BackoffBaseSeconds = 30,
        BackoffMaxSeconds = 900,
        MaxAttempts = 2
    };

    [Fact]
    public async Task Heartbeat_extends_the_lease()
    {
        var (task, attempt) = SeedRunning();

        var result = await CreateService().HeartbeatAsync(WorkerId, task.Id, attempt.Id, Token, CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Ok, result.Outcome);
        Assert.Equal(Now.AddSeconds(90), task.LeaseExpiresAtUtc);
        Assert.Equal(Now, task.LastHeartbeatAtUtc);
    }

    [Fact]
    public async Task Heartbeat_rejects_a_wrong_token()
    {
        var (task, attempt) = SeedRunning();

        var result = await CreateService().HeartbeatAsync(WorkerId, task.Id, attempt.Id, "wrong", CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Forbidden, result.Outcome);
        Assert.Equal("invalid-lease-token", result.Error);
        Assert.Equal(Now.AddSeconds(90), task.LeaseExpiresAtUtc);
    }

    [Fact]
    public async Task Heartbeat_rejects_another_worker()
    {
        var (task, attempt) = SeedRunning();

        var result = await CreateService().HeartbeatAsync(OtherWorkerId, task.Id, attempt.Id, Token, CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Forbidden, result.Outcome);
        Assert.Equal("worker-mismatch", result.Error);
    }

    [Fact]
    public async Task Heartbeat_rejects_an_expired_lease()
    {
        var (task, attempt) = SeedRunning(leaseExpiry: Now.AddSeconds(-1));

        var result = await CreateService().HeartbeatAsync(WorkerId, task.Id, attempt.Id, Token, CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Forbidden, result.Outcome);
        Assert.Equal("lease-expired", result.Error);
    }

    [Fact]
    public async Task Start_records_the_external_session_and_extends_the_lease()
    {
        var (task, attempt) = SeedRunning();

        var result = await CreateService().StartAsync(
            WorkerId,
            task.Id,
            attempt.Id,
            Token,
            "session-42",
            CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Ok, result.Outcome);
        Assert.Equal("session-42", attempt.ExternalSessionId);
        Assert.True(task.LeaseExpiresAtUtc > Now);
    }

    [Fact]
    public async Task Complete_marks_the_task_succeeded_and_clears_the_lease()
    {
        var (task, attempt) = SeedRunning();

        var result = await CreateService().CompleteAsync(WorkerId, task.Id, attempt.Id, Token, 0, CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Ok, result.Outcome);
        Assert.Equal(TaskStatus.Succeeded, task.Status);
        Assert.Equal(AttemptStatus.Succeeded, attempt.Status);
        Assert.Null(task.LeaseTokenHash);
        Assert.Null(task.LeaseOwnerWorkerId);
        Assert.NotNull(task.CompletedAtUtc);
        Assert.Equal(Now, attempt.FinishedAtUtc);
    }

    [Fact]
    public async Task Complete_is_rejected_after_a_cancel_request()
    {
        var (task, attempt) = SeedRunning(cancelRequested: Now.AddSeconds(-5));

        var result = await CreateService().CompleteAsync(WorkerId, task.Id, attempt.Id, Token, 0, CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Conflict, result.Outcome);
        Assert.Equal("cancel-requested", result.Error);
        Assert.Equal(TaskStatus.Running, task.Status);
    }

    [Fact]
    public async Task Retryable_failure_requeues_with_backoff()
    {
        var (task, attempt) = SeedRunning();

        var result = await CreateService().FailAsync(
            WorkerId,
            task.Id,
            attempt.Id,
            Token,
            "agent-error",
            "temporary failure",
            retryable: true,
            cancelled: false,
            CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Ok, result.Outcome);
        Assert.Equal(TaskStatus.RetryWait, task.Status);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.True(task.NextRunAtUtc > Now);
        Assert.Null(task.LeaseTokenHash);
        Assert.Equal("agent-error", task.ErrorCode);
    }

    [Fact]
    public async Task Last_attempt_failure_marks_the_task_failed()
    {
        var (task, attempt) = SeedRunning(attemptCount: 2, maxAttempts: 2);

        var result = await CreateService().FailAsync(
            WorkerId,
            task.Id,
            attempt.Id,
            Token,
            "agent-error",
            "fatal",
            retryable: true,
            cancelled: false,
            CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Ok, result.Outcome);
        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.NotNull(task.CompletedAtUtc);
    }

    [Fact]
    public async Task Cancelled_failure_marks_the_task_cancelled()
    {
        var (task, attempt) = SeedRunning();

        var result = await CreateService().FailAsync(
            WorkerId,
            task.Id,
            attempt.Id,
            Token,
            null,
            "cancelled by user",
            retryable: true,
            cancelled: true,
            CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Ok, result.Outcome);
        Assert.Equal(TaskStatus.Cancelled, task.Status);
        Assert.Equal(AttemptStatus.Cancelled, attempt.Status);
    }

    [Fact]
    public async Task Cancelling_a_queued_task_finishes_it_immediately()
    {
        var task = NewTask(TaskStatus.Queued);
        _store.Tasks.Add(task);

        var result = await CreateService().CancelAsync(task.Id, CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Ok, result.Outcome);
        Assert.Equal(TaskStatus.Cancelled, task.Status);
        Assert.NotNull(task.CompletedAtUtc);
    }

    [Fact]
    public async Task Cancelling_a_running_task_requests_cancellation_and_heartbeat_reports_it()
    {
        var (task, attempt) = SeedRunning();
        var service = CreateService();

        var cancel = await service.CancelAsync(task.Id, CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Ok, cancel.Outcome);
        Assert.True(cancel.CancelRequested);
        Assert.Equal(TaskStatus.Running, task.Status);
        Assert.NotNull(task.CancelRequestedAtUtc);

        var heartbeat = await service.HeartbeatAsync(WorkerId, task.Id, attempt.Id, Token, CancellationToken.None);
        Assert.True(heartbeat.CancelRequested);
    }

    [Fact]
    public async Task Cancelling_a_finished_task_is_idempotent()
    {
        var task = NewTask(TaskStatus.Succeeded);
        task.CompletedAtUtc = Now.AddMinutes(-1);
        _store.Tasks.Add(task);

        var result = await CreateService().CancelAsync(task.Id, CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Ok, result.Outcome);
        Assert.Equal(TaskStatus.Succeeded, task.Status);
    }

    [Fact]
    public async Task Unknown_task_returns_not_found()
    {
        var result = await CreateService().HeartbeatAsync(
            WorkerId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Token,
            CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Fail_after_a_cancel_request_is_recorded_as_cancelled()
    {
        var (task, attempt) = SeedRunning(cancelRequested: Now.AddSeconds(-5));

        var result = await CreateService().FailAsync(
            WorkerId,
            task.Id,
            attempt.Id,
            Token,
            "agent-error",
            "runner failed",
            retryable: true,
            cancelled: false,
            CancellationToken.None);

        Assert.Equal(TaskQueueOutcome.Ok, result.Outcome);
        Assert.Equal(TaskStatus.Cancelled, task.Status);
        Assert.Equal(AttemptStatus.Cancelled, attempt.Status);
    }

    [Fact]
    public async Task Oversized_runner_fields_are_bounded_before_saving()
    {
        var (task, attempt) = SeedRunning();
        var service = CreateService();

        var start = await service.StartAsync(
            WorkerId,
            task.Id,
            attempt.Id,
            Token,
            new string('s', 500),
            CancellationToken.None);
        Assert.Equal(TaskQueueOutcome.Ok, start.Outcome);
        Assert.Equal(200, attempt.ExternalSessionId!.Length);

        var fail = await service.FailAsync(
            WorkerId,
            task.Id,
            attempt.Id,
            Token,
            new string('e', 500),
            null,
            retryable: false,
            cancelled: false,
            CancellationToken.None);
        Assert.Equal(TaskQueueOutcome.Ok, fail.Outcome);
        Assert.Equal(100, attempt.ErrorCode!.Length);
    }

    private TaskQueueService CreateService() => new(
        _store,
        _time,
        Options.Create(_options),
        NullLogger<TaskQueueService>.Instance);

    private (AutomationTask Task, TaskAttempt Attempt) SeedRunning(
        int attemptCount = 1,
        int maxAttempts = 2,
        DateTime? leaseExpiry = null,
        DateTime? cancelRequested = null)
    {
        var task = NewTask(TaskStatus.Running);
        task.AttemptCount = attemptCount;
        task.MaxAttempts = maxAttempts;
        task.LeaseOwnerWorkerId = WorkerId;
        task.LeaseTokenHash = LeaseTokenService.Hash(Token);
        task.LeaseExpiresAtUtc = leaseExpiry ?? Now.AddSeconds(90);
        task.LastHeartbeatAtUtc = Now.AddSeconds(-10);
        task.StartedAtUtc = Now.AddSeconds(-10);
        task.CancelRequestedAtUtc = cancelRequested;

        var attempt = new TaskAttempt
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            WorkerId = WorkerId,
            AttemptNumber = attemptCount,
            Status = AttemptStatus.Running,
            StartedAtUtc = Now.AddSeconds(-10),
            CreatedAtUtc = Now.AddSeconds(-10),
            UpdatedAtUtc = Now.AddSeconds(-10)
        };

        _store.Tasks.Add(task);
        _store.Attempts.Add(attempt);
        return (task, attempt);
    }

    private static AutomationTask NewTask(TaskStatus status) => new()
    {
        Id = Guid.NewGuid(),
        TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
        ProjectId = Guid.Parse("0199c0de-0000-7000-8000-0000000000c1"),
        AgentId = Guid.Parse("0199c0de-0000-7000-8000-0000000000a1"),
        LogicalTaskKey = $"key-{Guid.NewGuid():N}",
        Status = status,
        Priority = "normal",
        Title = "test task",
        InputJson = "{}",
        AttemptCount = 0,
        MaxAttempts = 2,
        NextRunAtUtc = Now,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };
}
