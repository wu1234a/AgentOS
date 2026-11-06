using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

public sealed class RunnerApiTests
{
    private const string DevKey = "test-dev-key";
    private const string WorkerHeader = "X-AgentOS-Worker-Code";
    private const string Token = "lease-token-for-tests";

    [Fact]
    public async Task Claim_requires_a_worker_code_header()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/runners/claim", new { capacity = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Claim_rejects_an_unknown_worker()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory, workerCode: "missing-worker");

        using var response = await client.PostAsJsonAsync("/api/v1/runners/claim", new { capacity = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Claim_rejects_a_disabled_worker()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory, WorkerStatus.Disabled);
        using var client = CreateClient(factory, workerCode: worker.WorkerCode);

        using var response = await client.PostAsJsonAsync("/api/v1/runners/claim", new { capacity = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Claim_returns_the_claimed_tasks_and_lease()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory, WorkerStatus.Online);
        var taskId = Guid.NewGuid();
        factory.Claimer.Claims.Add(new ClaimedTask(
            taskId,
            "queue-test",
            "TASK-ABC",
            "Test task",
            "normal",
            """{"x":1}""",
            null,
            null,
            Guid.NewGuid(),
            null,
            null,
            2,
            1800,
            new TaskLease(Guid.NewGuid(), 1, "token-value", DateTime.UtcNow.AddSeconds(90))));

        using var client = CreateClient(factory, workerCode: worker.WorkerCode);
        using var response = await client.PostAsJsonAsync("/api/v1/runners/claim", new { capacity = 5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var tasks = body.GetProperty("tasks");
        Assert.Equal(1, tasks.GetArrayLength());
        Assert.Equal(taskId, tasks[0].GetProperty("taskId").GetGuid());
        Assert.Equal("token-value", tasks[0].GetProperty("lease").GetProperty("token").GetString());
        Assert.Equal(1, factory.Claimer.Calls);
    }

    [Fact]
    public async Task Claim_rejects_capacity_out_of_range()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory, WorkerStatus.Online);
        using var client = CreateClient(factory, workerCode: worker.WorkerCode);

        using var response = await client.PostAsJsonAsync("/api/v1/runners/claim", new { capacity = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_rejects_a_wrong_lease_token()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory, WorkerStatus.Online);
        var (task, attempt) = SeedRunningTask(factory, worker);

        using var client = CreateClient(factory, workerCode: worker.WorkerCode);
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/runners/tasks/{task.Id}/heartbeat",
            new { attemptId = attempt.Id, leaseToken = "wrong-token" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid-lease-token", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Complete_marks_the_task_succeeded()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory, WorkerStatus.Online);
        var (task, attempt) = SeedRunningTask(factory, worker);

        using var client = CreateClient(factory, workerCode: worker.WorkerCode);
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/runners/tasks/{task.Id}/complete",
            new { attemptId = attempt.Id, leaseToken = Token, exitCode = 0 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Succeeded", body.GetProperty("status").GetString());
        Assert.Equal(TaskStatus.Succeeded, task.Status);
        Assert.Null(task.LeaseTokenHash);
    }

    [Fact]
    public async Task Cancel_marks_a_queued_task_cancelled()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var task = SeedQueuedTask(factory);

        using var client = CreateClient(factory);
        using var response = await client.PostAsync($"/api/v1/tasks/{task.Id}/cancel", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
        Assert.Equal(TaskStatus.Cancelled, task.Status);
    }

    [Fact]
    public async Task Cancel_requires_authentication()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var task = SeedQueuedTask(factory);

        using var client = CreateClient(factory, authorize: false);
        using var response = await client.PostAsync($"/api/v1/tasks/{task.Id}/cancel", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Worker SeedWorker(AgentOSApiFactory factory, WorkerStatus status)
    {
        var worker = new Worker
        {
            Id = Guid.CreateVersion7(),
            WorkerCode = $"worker-{Guid.NewGuid():N}"[..20],
            DisplayName = "Test Runner",
            HostName = "localhost",
            Platform = "Windows",
            Architecture = "x64",
            Version = "1.0.0",
            Status = status,
            LastHeartbeatAtUtc = DateTime.UtcNow,
            RegisteredAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        factory.Queue.Workers.Add(worker);
        return worker;
    }

    private static AutomationTask SeedQueuedTask(AgentOSApiFactory factory)
    {
        var task = NewTask(TaskStatus.Queued);
        factory.Queue.Tasks.Add(task);
        return task;
    }

    private static (AutomationTask Task, TaskAttempt Attempt) SeedRunningTask(
        AgentOSApiFactory factory,
        Worker worker)
    {
        var task = NewTask(TaskStatus.Running);
        task.AttemptCount = 1;
        task.LeaseOwnerWorkerId = worker.Id;
        task.LeaseTokenHash = LeaseTokenService.Hash(Token);
        task.LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
        task.LastHeartbeatAtUtc = DateTime.UtcNow;
        task.StartedAtUtc = DateTime.UtcNow;

        var attempt = new TaskAttempt
        {
            Id = Guid.CreateVersion7(),
            TaskId = task.Id,
            WorkerId = worker.Id,
            AttemptNumber = 1,
            Status = AttemptStatus.Running,
            StartedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        factory.Queue.Tasks.Add(task);
        factory.Queue.Attempts.Add(attempt);
        return (task, attempt);
    }

    private static AutomationTask NewTask(TaskStatus status) => new()
    {
        Id = Guid.CreateVersion7(),
        TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
        ProjectId = Guid.CreateVersion7(),
        AgentId = Guid.CreateVersion7(),
        LogicalTaskKey = $"key-{Guid.NewGuid():N}",
        Status = status,
        Priority = "normal",
        Title = "api test task",
        InputJson = "{}",
        AttemptCount = 0,
        MaxAttempts = 2,
        NextRunAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static HttpClient CreateClient(
        AgentOSApiFactory factory,
        bool authorize = true,
        string? workerCode = null)
    {
        var client = factory.CreateClient();

        if (authorize)
        {
            client.DefaultRequestHeaders.Add("X-AgentOS-Dev-Key", DevKey);
        }

        if (workerCode is not null)
        {
            client.DefaultRequestHeaders.Add(WorkerHeader, workerCode);
        }

        return client;
    }
}
