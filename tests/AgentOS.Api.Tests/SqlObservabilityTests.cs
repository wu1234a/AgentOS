using AgentOS.Application.Observability;
using AgentOS.Domain.Agents;
using AgentOS.Domain.Artifacts;
using AgentOS.Domain.Events;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Usage;
using AgentOS.Domain.Workers;
using AgentOS.Infrastructure.Artifacts;
using AgentOS.Infrastructure.Observability;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

/// <summary>AOS-008 acceptance tests against a real SQL Server.</summary>
[Collection("SqlServer")]
public sealed class SqlObservabilityTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Dashboard_counts_match_the_database()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetAsync(connectionString);

        await using var context = NewContext(connectionString);
        var project = await SeedProjectAsync(context);
        var agent = await SeedAgentAsync(context, project);
        var worker = await SeedWorkerAsync(context);
        var evt = SeedEvent(project, dedupeKey: "evt-1");
        context.Events.Add(evt);
        evt.DuplicateCount = 2;

        var succeeded = SeedTask(project, agent, evt, TaskStatus.Succeeded, Now.AddMinutes(-10), Now.AddMinutes(-9), Now.AddMinutes(-8));
        var failed = SeedTask(project, agent, evt, TaskStatus.Failed, Now.AddMinutes(-7), Now.AddMinutes(-6), Now.AddMinutes(-5));
        var queued = SeedTask(project, agent, null, TaskStatus.Queued, Now.AddMinutes(-1), null, null);
        context.Tasks.AddRange(succeeded, failed, queued);

        var attempt = SeedAttempt(succeeded, worker, AttemptStatus.Succeeded, Now.AddMinutes(-9));
        context.TaskAttempts.Add(attempt);

        var lostAttempt = SeedAttempt(failed, worker, AttemptStatus.Lost, Now.AddMinutes(-6));
        context.TaskAttempts.Add(lostAttempt);

        context.TaskLogs.Add(new TaskLog
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            EventId = evt.Id,
            TaskId = failed.Id,
            AttemptId = lostAttempt.Id,
            WorkerId = worker.Id,
            Sequence = 1,
            Level = "error",
            Component = "runner",
            Message = "Lease expired",
            CreatedAtUtc = Now.AddMinutes(-5)
        });

        context.UsageRecords.Add(new UsageRecord
        {
            Id = Guid.CreateVersion7(),
            TaskId = succeeded.Id,
            AttemptId = attempt.Id,
            WorkerId = worker.Id,
            Provider = "codex",
            Model = "test-model",
            InputTokens = 100,
            CachedInputTokens = 20,
            OutputTokens = 30,
            TotalTokens = 130,
            Source = "adapter",
            CreatedAtUtc = Now
        });

        await context.SaveChangesAsync();

        var store = new ObservabilityQueryStore(context);
        var stats = await store.GetDashboardAsync(Now, CancellationToken.None);

        Assert.Equal(1, stats.TotalEvents);
        Assert.Equal(2, stats.DuplicateEvents);
        Assert.Equal(3, stats.TotalTasks);
        Assert.Equal(1, stats.QueuedTasks);
        Assert.Equal(1, stats.SucceededTasks);
        Assert.Equal(1, stats.FailedTasks);
        Assert.Equal(1, stats.OnlineRunners);
        Assert.Equal(1, stats.LeaseExpiryCount);
        Assert.Equal(50d, stats.SuccessRate);
        Assert.Single(stats.RecentFailures);
    }

    [SkippableFact]
    public async Task Task_detail_exposes_attempts_logs_artifacts_and_usage()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetAsync(connectionString);

        await using var context = NewContext(connectionString);
        var project = await SeedProjectAsync(context);
        var agent = await SeedAgentAsync(context, project);
        var worker = await SeedWorkerAsync(context);
        var task = SeedTask(project, agent, null, TaskStatus.Succeeded, Now.AddMinutes(-10), Now.AddMinutes(-9), Now.AddMinutes(-8));
        context.Tasks.Add(task);

        var attempt = SeedAttempt(task, worker, AttemptStatus.Succeeded, Now.AddMinutes(-9));
        context.TaskAttempts.Add(attempt);

        context.TaskLogs.AddRange(
            new TaskLog
            {
                Id = Guid.CreateVersion7(),
                ProjectId = project.Id,
                TaskId = task.Id,
                AttemptId = attempt.Id,
                WorkerId = worker.Id,
                Sequence = 1,
                Level = "info",
                Component = "runner",
                Message = "started",
                CreatedAtUtc = Now.AddMinutes(-9)
            },
            new TaskLog
            {
                Id = Guid.CreateVersion7(),
                ProjectId = project.Id,
                TaskId = task.Id,
                AttemptId = attempt.Id,
                WorkerId = worker.Id,
                Sequence = 2,
                Level = "error",
                Component = "adapter",
                Message = "boom",
                CreatedAtUtc = Now.AddMinutes(-8)
            });

        context.Artifacts.Add(new Artifact
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            TaskId = task.Id,
            AttemptId = attempt.Id,
            WorkerId = worker.Id,
            Name = "stdout.log",
            RelativePath = $"artifacts/{task.Id:N}/stdout.log",
            Sha256 = new string('a', 64),
            SizeBytes = 42,
            MediaType = "text/plain",
            CreatedAtUtc = Now
        });

        await context.SaveChangesAsync();

        var store = new ObservabilityQueryStore(context);
        var detail = await store.GetTaskAsync(task.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Single(detail.Attempts);
        Assert.Equal("worker-1", detail.Attempts[0].WorkerCode);
        Assert.Single(detail.Artifacts);
        Assert.Equal(2, detail.RecentLogs.Count);

        var errorLogs = await store.GetAttemptLogsAsync(
            task.Id,
            attempt.Id,
            "error",
            0,
            10,
            CancellationToken.None);
        Assert.Single(errorLogs.Items);
        Assert.Equal("boom", errorLogs.Items[0].Message);

        var firstPage = await store.GetAttemptLogsAsync(task.Id, attempt.Id, null, 0, 1, CancellationToken.None);
        Assert.Single(firstPage.Items);
        Assert.Equal(1, firstPage.NextSequence);

        var secondPage = await store.GetAttemptLogsAsync(
            task.Id,
            attempt.Id,
            null,
            firstPage.NextSequence!.Value,
            10,
            CancellationToken.None);
        Assert.Single(secondPage.Items);
        Assert.Equal(2, secondPage.Items[0].Sequence);
    }

    [SkippableFact]
    public async Task Event_detail_links_tasks_and_task_list_filters_status()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetAsync(connectionString);

        await using var context = NewContext(connectionString);
        var project = await SeedProjectAsync(context);
        var agent = await SeedAgentAsync(context, project);
        var evt = SeedEvent(project, dedupeKey: "evt-42");
        context.Events.Add(evt);

        var task = SeedTask(project, agent, evt, TaskStatus.Queued, Now, null, null);
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        var store = new ObservabilityQueryStore(context);
        var detail = await store.GetEventAsync(evt.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Single(detail.Tasks);
        Assert.Equal(evt.Id, task.EventId);

        var (queued, total) = await store.ListTasksAsync("Queued", project.Id, 10, 0, CancellationToken.None);
        Assert.Equal(1, total);
        Assert.Single(queued);

        var (failed, failedTotal) = await store.ListTasksAsync("Failed", null, 10, 0, CancellationToken.None);
        Assert.Equal(0, failedTotal);
        Assert.Empty(failed);
    }

    [SkippableFact]
    public async Task Artifact_store_enforces_root_limits_and_hashes_content()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);
        await ResetAsync(connectionString);

        await using var context = NewContext(connectionString);
        var project = await SeedProjectAsync(context);
        var agent = await SeedAgentAsync(context, project);
        var worker = await SeedWorkerAsync(context);
        var task = SeedTask(project, agent, null, TaskStatus.Running, Now, Now, null);
        context.Tasks.Add(task);
        var attempt = SeedAttempt(task, worker, AttemptStatus.Running, Now);
        context.TaskAttempts.Add(attempt);
        await context.SaveChangesAsync();

        var root = Path.Combine(Path.GetTempPath(), "agentos-artifact-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new ArtifactOptions { RootPath = root, MaxBytes = 32 });
        var store = new ArtifactStore(context, options, TimeProvider.System, NullLogger<ArtifactStore>.Instance);

        await using var content = new MemoryStream("hello artifact"u8.ToArray());
        var artifact = await store.SaveAsync(
            project.Id,
            task.Id,
            attempt.Id,
            worker.Id,
            "../evil.log",
            "text/plain",
            content,
            CancellationToken.None);

        Assert.Equal("evil.log", artifact.Name);
        Assert.Equal(64, artifact.Sha256.Length);
        Assert.True(File.Exists(Path.Combine(root, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar))));

        var opened = await store.OpenAsync(artifact.Id, CancellationToken.None);
        Assert.NotNull(opened);
        using var reader = new StreamReader(opened.Value.Stream);
        Assert.Equal("hello artifact", await reader.ReadToEndAsync());

        await using var tooLarge = new MemoryStream(new byte[33]);
        await Assert.ThrowsAsync<ArtifactValidationException>(() => store.SaveAsync(
            project.Id, task.Id, attempt.Id, worker.Id, "big.bin", "application/octet-stream", tooLarge, CancellationToken.None));

        await using var badType = new MemoryStream("x"u8.ToArray());
        await Assert.ThrowsAsync<ArtifactValidationException>(() => store.SaveAsync(
            project.Id, task.Id, attempt.Id, worker.Id, "x.exe", "application/x-msdownload", badType, CancellationToken.None));
    }

    [Fact]
    public void Log_sanitizer_redacts_credentials()
    {
        var sanitized = TaskLogSanitizer.Redact(
            "token sk-abcdefghijklmnop password=hunter2 Authorization: Bearer abc.def Cookie: session=xyz");

        Assert.DoesNotContain("sk-abcdefghijklmnop", sanitized);
        Assert.DoesNotContain("hunter2", sanitized);
        Assert.DoesNotContain("abc.def", sanitized);
        Assert.DoesNotContain("session=xyz", sanitized);
        Assert.Contains("[REDACTED]", sanitized);
    }

    private static AgentOSDbContext NewContext(string connectionString)
        => new(new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);

    private static async Task MigrateAsync(string connectionString)
    {
        await using var context = NewContext(connectionString);
        await context.Database.MigrateAsync();
    }

    private static async Task ResetAsync(string connectionString)
    {
        await using var context = NewContext(connectionString);
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM [TaskLogs]; DELETE FROM [UsageRecords]; DELETE FROM [Artifacts]; DELETE FROM [TaskAttempts]; DELETE FROM [Tasks]; DELETE FROM [Events];");
    }

    private static async Task<Project> SeedProjectAsync(AgentOSDbContext context)
    {
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Observability",
            Slug = $"obs-{Guid.NewGuid():N}"[..20],
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project;
    }

    private static async Task<Agent> SeedAgentAsync(AgentOSDbContext context, Project project)
    {
        var agent = new Agent
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Code = $"agent-{Guid.NewGuid():N}"[..16],
            DisplayName = "Coder",
            Role = "coder",
            PermissionProfile = "read-only",
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Agents.Add(agent);
        await context.SaveChangesAsync();
        return agent;
    }

    private static async Task<Worker> SeedWorkerAsync(AgentOSDbContext context)
    {
        var existing = await context.Workers.FirstOrDefaultAsync(worker => worker.WorkerCode == "worker-1");
        if (existing is not null)
        {
            return existing;
        }

        var worker = new Worker
        {
            Id = Guid.CreateVersion7(),
            WorkerCode = "worker-1",
            DisplayName = "Runner",
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

    private static Event SeedEvent(Project project, string dedupeKey) => new()
    {
        Id = Guid.CreateVersion7(),
        ProjectId = project.Id,
        Source = "github",
        EventType = "issue.opened",
        SchemaVersion = "1.0",
        DedupeKey = dedupeKey,
        Summary = "Issue opened",
        PayloadJson = """{"issue":42}""",
        Status = EventStatus.Processed,
        OccurredAtUtc = Now.AddMinutes(-20),
        ReceivedAtUtc = Now.AddMinutes(-20)
    };

    private static AutomationTask SeedTask(
        Project project,
        Agent agent,
        Event? evt,
        TaskStatus status,
        DateTime createdAt,
        DateTime? startedAt,
        DateTime? completedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
        ProjectId = project.Id,
        EventId = evt?.Id,
        AgentId = agent.Id,
        LogicalTaskKey = $"obs-{Guid.NewGuid():N}",
        Status = status,
        Priority = "normal",
        Title = "Observability task",
        InputJson = "{}",
        AttemptCount = status == TaskStatus.Queued ? 0 : 1,
        MaxAttempts = 2,
        NextRunAtUtc = createdAt,
        CreatedAtUtc = createdAt,
        StartedAtUtc = startedAt,
        CompletedAtUtc = completedAt,
        UpdatedAtUtc = completedAt ?? createdAt
    };

    private static TaskAttempt SeedAttempt(
        AutomationTask task,
        Worker worker,
        AttemptStatus status,
        DateTime startedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        TaskId = task.Id,
        WorkerId = worker.Id,
        AttemptNumber = 1,
        Status = status,
        StartedAtUtc = startedAt,
        FinishedAtUtc = status is AttemptStatus.Succeeded or AttemptStatus.Failed ? startedAt.AddMinutes(1) : null,
        ExitCode = status == AttemptStatus.Succeeded ? 0 : null,
        DurationMs = 60000,
        CreatedAtUtc = startedAt,
        UpdatedAtUtc = startedAt
    };

    private static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            "Set AGENTOS_SQL_TEST_CONNECTION to a disposable database.");
        return connectionString!;
    }
}
