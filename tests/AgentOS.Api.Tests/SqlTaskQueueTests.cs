using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Runners;
using AgentOS.Application.Security;
using AgentOS.Infrastructure.Runners;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Agents;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

/// <summary>AOS-005 acceptance tests against a real SQL Server.</summary>
[Collection("SqlServer")]
public sealed class SqlTaskQueueTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 8, 0, 0, DateTimeKind.Utc);

    private static readonly TaskQueueOptions QueueOptions = new()
    {
        LeaseDurationSeconds = 90,
        MaxAttempts = 2,
        BackoffBaseSeconds = 1,
        BackoffMaxSeconds = 5,
        ClaimBatchLimit = 10
    };

    [SkippableFact]
    public async Task Concurrent_claims_hand_each_task_to_exactly_one_worker()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetQueueAsync(connectionString);

        await using var setup = NewContext(connectionString);
        var project = await SeedProjectAsync(setup);
        var worker = await SeedWorkerAsync(setup);
        await SeedTasksAsync(setup, project, count: 12);

        var claimers = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => ClaimAsync(connectionString, worker.Id, capacity: 4)))
            .ToArray();

        var results = await Task.WhenAll(claimers);
        var concurrentClaims = results.SelectMany(result => result).ToArray();

        // No task may be handed out twice under concurrency, and every returned lease must be
        // backed by exactly one attempt row.
        Assert.Equal(concurrentClaims.Length, concurrentClaims.Select(task => task.TaskId).Distinct().Count());
        Assert.Equal(concurrentClaims.Length, concurrentClaims.Select(task => task.Lease.Token).Distinct().Count());
        Assert.All(concurrentClaims, task => Assert.Equal(1, task.Lease.AttemptNumber));

        // Drain the rest sequentially and verify the queue reached a consistent state.
        var drained = await ClaimAsync(connectionString, worker.Id, capacity: 20);
        var allClaimed = concurrentClaims.Concat(drained).ToArray();

        Assert.Equal(12, allClaimed.Select(task => task.TaskId).Distinct().Count());
        Assert.Equal(12, allClaimed.Length);

        await using var verify = NewContext(connectionString);
        Assert.Equal(12, await verify.Tasks.CountAsync(task => task.Status == TaskStatus.Running));
        Assert.Equal(12, await verify.TaskAttempts.CountAsync(attempt => attempt.Status == AttemptStatus.Running));
        Assert.Equal(
            12,
            await verify.Tasks.CountAsync(task => task.LeaseOwnerWorkerId == worker.Id && task.LeaseTokenHash != null));
    }

    [SkippableFact]
    public async Task Claim_honours_priority_and_skips_retries_that_are_not_due()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetQueueAsync(connectionString);

        await using var setup = NewContext(connectionString);
        var project = await SeedProjectAsync(setup);
        var worker = await SeedWorkerAsync(setup);

        var low = await SeedTaskAsync(setup, project, "low", Now);
        var urgent = await SeedTaskAsync(setup, project, "urgent", Now);
        var normal = await SeedTaskAsync(setup, project, "normal", Now);
        var futureRetry = await SeedTaskAsync(setup, project, "urgent", Now.AddMinutes(5), TaskStatus.RetryWait);

        var claimed = await ClaimAsync(connectionString, worker.Id, capacity: 10);
        var order = claimed.Select(task => task.TaskId).ToArray();

        Assert.Equal([urgent, normal, low], order);
        Assert.DoesNotContain(futureRetry, order);
    }

    [SkippableFact]
    public async Task Heartbeat_extend_then_complete_persists_attempt_and_clears_lease()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetQueueAsync(connectionString);

        await using var setup = NewContext(connectionString);
        var project = await SeedProjectAsync(setup);
        var worker = await SeedWorkerAsync(setup);
        var taskId = await SeedTaskAsync(setup, project, "normal", Now);

        var claimed = (await ClaimAsync(connectionString, worker.Id, capacity: 1)).Single();

        await using var context = NewContext(connectionString);
        var service = CreateQueueService(context, new ManualTimeProvider(Now.AddSeconds(30)));
        var store = new TaskQueueStore(context);

        var rejected = await service.HeartbeatAsync(
            worker.Id,
            taskId,
            claimed.Lease.AttemptId,
            "not-the-token",
            CancellationToken.None);
        Assert.Equal(TaskQueueOutcome.Forbidden, rejected.Outcome);

        var heartbeat = await service.HeartbeatAsync(
            worker.Id,
            taskId,
            claimed.Lease.AttemptId,
            claimed.Lease.Token,
            CancellationToken.None);
        Assert.Equal(TaskQueueOutcome.Ok, heartbeat.Outcome);
        Assert.Equal(Now.AddSeconds(120), heartbeat.LeaseExpiresAtUtc);

        var completed = await service.CompleteAsync(
            worker.Id,
            taskId,
            claimed.Lease.AttemptId,
            claimed.Lease.Token,
            0,
            CancellationToken.None);
        Assert.Equal(TaskQueueOutcome.Ok, completed.Outcome);

        var storedTask = await store.GetTaskAsync(taskId, CancellationToken.None);
        Assert.Equal(TaskStatus.Succeeded, storedTask!.Status);
        Assert.Null(storedTask.LeaseTokenHash);
        Assert.Null(storedTask.LeaseOwnerWorkerId);

        var attempt = await context.TaskAttempts.SingleAsync(candidate => candidate.Id == claimed.Lease.AttemptId);
        Assert.Equal(AttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(0, attempt.ExitCode);
    }

    [SkippableFact]
    public async Task Expired_lease_is_recovered_then_retried_by_a_new_claim()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetQueueAsync(connectionString);

        await using var setup = NewContext(connectionString);
        var project = await SeedProjectAsync(setup);
        var worker = await SeedWorkerAsync(setup);
        var taskId = await SeedTaskAsync(setup, project, "normal", Now);

        await ClaimAsync(connectionString, worker.Id, capacity: 1);

        // Force the lease into the past, as if the worker disappeared.
        await using (var expire = NewContext(connectionString))
        {
            var task = await expire.Tasks.SingleAsync(candidate => candidate.Id == taskId);
            task.LeaseExpiresAtUtc = Now.AddSeconds(-1);
            await expire.SaveChangesAsync();
        }

        var clock = new ManualTimeProvider(Now);
        await using (var recoveryContext = NewContext(connectionString))
        {
            var recovery = new LeaseRecoveryService(
                new TaskQueueStore(recoveryContext),
                clock,
                Options.Create(QueueOptions),
                NullLogger<LeaseRecoveryService>.Instance);
            var stats = await recovery.RecoverAsync(CancellationToken.None);
            Assert.Equal(new LeaseRecoveryStats(1, 0, 0), stats);
        }

        await using (var verify = NewContext(connectionString))
        {
            var task = await verify.Tasks.SingleAsync(candidate => candidate.Id == taskId);
            Assert.Equal(TaskStatus.RetryWait, task.Status);
            Assert.True(task.NextRunAtUtc > Now);

            var attempt = await verify.TaskAttempts.SingleAsync(candidate => candidate.TaskId == taskId);
            Assert.Equal(AttemptStatus.Lost, attempt.Status);
        }

        // Not due yet.
        Assert.Empty(await ClaimAsync(connectionString, worker.Id, capacity: 1, clock));

        // Due after the backoff: a second attempt is created and the first one is preserved.
        clock.Advance(TimeSpan.FromMinutes(2));
        var second = await ClaimAsync(connectionString, worker.Id, capacity: 1, clock);

        var retried = Assert.Single(second);
        Assert.Equal(taskId, retried.TaskId);
        Assert.Equal(2, retried.Lease.AttemptNumber);

        await using var finalCheck = NewContext(connectionString);
        Assert.Equal(2, await finalCheck.TaskAttempts.CountAsync(candidate => candidate.TaskId == taskId));
    }

    [SkippableFact]
    public async Task Cancelled_running_task_is_finalized_when_its_lease_expires()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetQueueAsync(connectionString);

        await using var setup = NewContext(connectionString);
        var project = await SeedProjectAsync(setup);
        var worker = await SeedWorkerAsync(setup);
        var taskId = await SeedTaskAsync(setup, project, "normal", Now);
        var clock = new ManualTimeProvider(Now);

        await ClaimAsync(connectionString, worker.Id, capacity: 1, clock);

        await using (var cancelContext = NewContext(connectionString))
        {
            var service = CreateQueueService(cancelContext, clock);
            var cancelled = await service.CancelAsync(taskId, CancellationToken.None);
            Assert.Equal(TaskQueueOutcome.Ok, cancelled.Outcome);
            Assert.True(cancelled.CancelRequested);
        }

        // The runner dies before acknowledging the cancellation; the lease expires.
        await using (var expire = NewContext(connectionString))
        {
            var task = await expire.Tasks.SingleAsync(candidate => candidate.Id == taskId);
            task.LeaseExpiresAtUtc = Now.AddSeconds(-1);
            await expire.SaveChangesAsync();
        }

        await using (var recoveryContext = NewContext(connectionString))
        {
            var recovery = new LeaseRecoveryService(
                new TaskQueueStore(recoveryContext),
                clock,
                Options.Create(QueueOptions),
                NullLogger<LeaseRecoveryService>.Instance);
            var stats = await recovery.RecoverAsync(CancellationToken.None);
            Assert.Equal(new LeaseRecoveryStats(0, 0, 1), stats);
        }

        await using (var verify = NewContext(connectionString))
        {
            var task = await verify.Tasks.SingleAsync(candidate => candidate.Id == taskId);
            Assert.Equal(TaskStatus.Cancelled, task.Status);

            var attempt = await verify.TaskAttempts.SingleAsync(candidate => candidate.TaskId == taskId);
            Assert.Equal(AttemptStatus.Cancelled, attempt.Status);
        }

        // Cancelled work must never become claimable again.
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Empty(await ClaimAsync(connectionString, worker.Id, capacity: 5, clock));
    }

    [SkippableFact]
    public async Task Registration_rotates_runner_credentials()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        var workerCode = $"runner-{Guid.NewGuid():N}"[..24];
        var request = new RunnerRegistrationRequest(
            workerCode,
            "SQL Runner",
            "localhost",
            "Windows",
            "x64",
            "1.0.0",
            "{}");

        RunnerRegistrationResult first;
        RunnerRegistrationResult second;

        await using (var context = NewContext(connectionString))
        {
            var service = new RunnerRegistrationService(
                new RunnerRegistrationStore(context),
                Options.Create(new RunnerRegistrationOptions { RegistrationKey = "sql-key" }),
                new ManualTimeProvider(Now),
                NullLogger<RunnerRegistrationService>.Instance);

            first = await service.RegisterAsync("sql-key", request, CancellationToken.None);
            second = await service.RegisterAsync("sql-key", request, CancellationToken.None);
        }

        Assert.Equal(RunnerRegistrationOutcome.Registered, first.Outcome);
        Assert.Equal(RunnerRegistrationOutcome.Registered, second.Outcome);
        Assert.NotEqual(first.Registration!.ClientSecret, second.Registration!.ClientSecret);

        await using var verify = NewContext(connectionString);
        var clients = await verify.ApiClients
            .Where(client => client.WorkerId == first.Registration.WorkerId)
            .ToListAsync();

        // Re-registration rotates the secret of the single stable runner client.
        var client = Assert.Single(clients);
        Assert.True(client.IsEnabled);
        Assert.Equal(ClientSecretHasher.Hash(second.Registration.ClientSecret), client.SecretHash);
        Assert.False(ClientSecretHasher.Matches(client.SecretHash, first.Registration.ClientSecret));
        Assert.NotEqual(second.Registration.ClientSecret, client.SecretHash);
    }

    [SkippableFact]
    public async Task Disabled_worker_cannot_claim_and_heartbeat_keeps_it_disabled()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetQueueAsync(connectionString);

        await using var setup = NewContext(connectionString);
        var project = await SeedProjectAsync(setup);
        var worker = await SeedWorkerAsync(setup);
        await SeedTaskAsync(setup, project, "normal", Now);

        worker.Status = WorkerStatus.Disabled;
        await setup.SaveChangesAsync();

        Assert.Empty(await ClaimAsync(connectionString, worker.Id, capacity: 5));

        await using (var storeContext = NewContext(connectionString))
        {
            var store = new TaskQueueStore(storeContext);
            Assert.True(await store.TouchWorkerAsync(worker.Id, Now, CancellationToken.None));
        }

        await using var verify = NewContext(connectionString);
        var stored = await verify.Workers.SingleAsync(candidate => candidate.Id == worker.Id);
        Assert.Equal(WorkerStatus.Disabled, stored.Status);
    }

    [SkippableFact]
    public async Task Task_logs_are_idempotent_per_attempt_sequence()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetQueueAsync(connectionString);

        await using var setup = NewContext(connectionString);
        var project = await SeedProjectAsync(setup);
        var worker = await SeedWorkerAsync(setup);
        var taskId = await SeedTaskAsync(setup, project, "normal", Now);

        var claimed = (await ClaimAsync(connectionString, worker.Id, capacity: 1)).Single();
        var batch = new[]
        {
            new TaskLogEntry(1, "info", "first line", Now),
            new TaskLogEntry(2, "warn", "second line", Now)
        };

        await using (var logContext = NewContext(connectionString))
        {
            var store = new TaskLogStore(logContext);
            await store.AddAsync(taskId, claimed.Lease.AttemptId, worker.Id, batch, CancellationToken.None);

            // A retried upload must not duplicate the stream.
            await store.AddAsync(taskId, claimed.Lease.AttemptId, worker.Id, batch, CancellationToken.None);
        }

        await using var verify = NewContext(connectionString);
        var logs = await verify.TaskLogs
            .Where(log => log.TaskId == taskId)
            .OrderBy(log => log.Sequence)
            .ToListAsync();

        Assert.Equal(2, logs.Count);
        Assert.Equal([1, 2], logs.Select(log => log.Sequence));
        Assert.Equal("second line", logs[1].Message);
    }

    [SkippableFact]
    public async Task Fail_retries_until_max_attempts_then_marks_the_task_failed()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetQueueAsync(connectionString);

        await using var setup = NewContext(connectionString);
        var project = await SeedProjectAsync(setup);
        var worker = await SeedWorkerAsync(setup);
        var taskId = await SeedTaskAsync(setup, project, "normal", Now);
        var clock = new ManualTimeProvider(Now);

        var firstClaim = (await ClaimAsync(connectionString, worker.Id, capacity: 1, clock)).Single();

        await using (var context = NewContext(connectionString))
        {
            var service = CreateQueueService(context, clock);
            var failed = await service.FailAsync(
                worker.Id,
                taskId,
                firstClaim.Lease.AttemptId,
                firstClaim.Lease.Token,
                "agent-error",
                "try again",
                retryable: true,
                cancelled: false,
                CancellationToken.None);
            Assert.Equal(TaskQueueOutcome.Ok, failed.Outcome);
            Assert.Equal(TaskStatus.RetryWait, failed.Task!.Status);
        }

        clock.Advance(TimeSpan.FromMinutes(2));
        var secondClaim = (await ClaimAsync(connectionString, worker.Id, capacity: 1, clock)).Single();
        Assert.Equal(2, secondClaim.Lease.AttemptNumber);

        await using (var context = NewContext(connectionString))
        {
            var service = CreateQueueService(context, clock);
            var failed = await service.FailAsync(
                worker.Id,
                taskId,
                secondClaim.Lease.AttemptId,
                secondClaim.Lease.Token,
                "agent-error",
                "still failing",
                retryable: true,
                cancelled: false,
                CancellationToken.None);

            Assert.Equal(TaskStatus.Failed, failed.Task!.Status);
        }

        await using var verify = NewContext(connectionString);
        var attempts = await verify.TaskAttempts
            .Where(candidate => candidate.TaskId == taskId)
            .OrderBy(candidate => candidate.AttemptNumber)
            .ToListAsync();

        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, attempt => Assert.Equal(AttemptStatus.Failed, attempt.Status));
    }

    private static TaskQueueService CreateQueueService(AgentOSDbContext context, TimeProvider clock) => new(
        new TaskQueueStore(context),
        new TaskLogStore(context),
        clock,
        Options.Create(QueueOptions),
        NullLogger<TaskQueueService>.Instance);

    private static async Task<List<ClaimedTask>> ClaimAsync(
        string connectionString,
        Guid workerId,
        int capacity,
        TimeProvider? clock = null)
    {
        await using var context = NewContext(connectionString);
        var claimer = new TaskClaimer(
            context,
            new LeaseTokenService(),
            clock ?? new ManualTimeProvider(Now),
            Options.Create(QueueOptions),
            new FakeSkillContentReader(),
            NullLogger<TaskClaimer>.Instance);

        return (await claimer.ClaimAsync(workerId, capacity, CancellationToken.None)).ToList();
    }

    private static async Task<Project> SeedProjectAsync(AgentOSDbContext context)
    {
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Queue Integration",
            Slug = $"queue-{Guid.NewGuid():N}"[..24],
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };

        context.Projects.Add(project);
        context.Agents.Add(new Agent
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Code = $"queue-agent-{Guid.NewGuid():N}"[..24],
            DisplayName = "Queue Agent",
            Role = "coder",
            PermissionProfile = "read-only",
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        });

        await context.SaveChangesAsync();
        return project;
    }

    private static async Task<Worker> SeedWorkerAsync(AgentOSDbContext context)
    {
        var worker = new Worker
        {
            Id = Guid.CreateVersion7(),
            WorkerCode = $"worker-{Guid.NewGuid():N}"[..24],
            DisplayName = "Test Runner",
            HostName = "localhost",
            Platform = "Windows",
            Architecture = "x64",
            Version = "1.0.0",
            Status = WorkerStatus.Online,
            LastHeartbeatAtUtc = Now,
            RegisteredAtUtc = Now,
            UpdatedAtUtc = Now
        };

        context.Workers.Add(worker);
        await context.SaveChangesAsync();
        return worker;
    }

    private static async Task<List<Guid>> SeedTasksAsync(
        AgentOSDbContext context,
        Project project,
        int count)
    {
        var priorities = new[] { "urgent", "high", "normal", "low" };
        var ids = new List<Guid>();

        for (var index = 0; index < count; index++)
        {
            ids.Add(await SeedTaskAsync(
                context,
                project,
                priorities[index % priorities.Length],
                Now,
                TaskStatus.Queued,
                save: false));
        }

        await context.SaveChangesAsync();
        return ids;
    }

    private static async Task<Guid> SeedTaskAsync(
        AgentOSDbContext context,
        Project project,
        string priority,
        DateTime nextRunAtUtc,
        TaskStatus status = TaskStatus.Queued,
        bool save = true)
    {
        var agentId = await context.Agents
            .Where(agent => agent.ProjectId == project.Id)
            .Select(agent => agent.Id)
            .FirstAsync();

        var task = new AutomationTask
        {
            Id = Guid.CreateVersion7(),
            TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
            ProjectId = project.Id,
            AgentId = agentId,
            LogicalTaskKey = $"queue-{Guid.NewGuid():N}",
            Status = status,
            Priority = priority,
            Title = $"queue test {priority}",
            InputJson = "{}",
            AttemptCount = 0,
            MaxAttempts = 2,
            TimeoutSeconds = 1800,
            NextRunAtUtc = nextRunAtUtc,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };

        context.Tasks.Add(task);

        if (save)
        {
            await context.SaveChangesAsync();
        }

        return task.Id;
    }

    private static async Task ResetQueueAsync(string connectionString)
    {
        // The database is disposable; clear queue state so each test starts from a known slate.
        await using var context = NewContext(connectionString);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM [TaskLogs]; DELETE FROM [TaskAttempts]; DELETE FROM [Tasks];");
    }

    private static async Task MigrateAsync(string connectionString)
    {
        await using var context = NewContext(connectionString);
        await context.Database.MigrateAsync();
    }

    private static AgentOSDbContext NewContext(string connectionString)
        => new(new DbContextOptionsBuilder<AgentOSDbContext>()
            .UseSqlServer(connectionString)
            .Options);

    private static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            "Set AGENTOS_SQL_TEST_CONNECTION to a disposable database to run the SQL task queue tests.");
        return connectionString!;
    }
}
