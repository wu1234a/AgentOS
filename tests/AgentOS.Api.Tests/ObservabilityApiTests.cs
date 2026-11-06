using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AgentOS.Application.Observability;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Artifacts;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentOS.Api.Tests;

public sealed class ObservabilityApiTests
{
    private const string DevKey = "test-dev-key";

    [Fact]
    public async Task Dashboard_requires_authentication()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/dashboard/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_returns_the_stats()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/dashboard/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(4, body.GetProperty("totalEvents").GetInt32());
        Assert.Equal(100, body.GetProperty("successRate").GetDouble());
    }

    [Fact]
    public async Task Task_list_and_detail_are_exposed()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var summary = NewSummary();
        factory.Observability.Tasks.Add(summary);
        factory.Observability.TaskDetail = new TaskDetail(summary, [], [], null, []);

        using var client = CreateClient(factory);

        var list = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/tasks?status=Queued");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(summary.TaskCode, list.GetProperty("items")[0].GetProperty("taskCode").GetString());

        var detail = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/tasks/{summary.Id}");
        Assert.Equal(summary.Id, detail.GetProperty("task").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Unknown_task_returns_404()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/tasks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Logs_are_paged()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        factory.Observability.LogPages.Add(new LogPage(
            [new TaskLogView(Guid.NewGuid(), 1, "info", "runner", "hello", DateTime.UtcNow)],
            1));

        using var client = CreateClient(factory);
        var page = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/tasks/{Guid.NewGuid()}/logs");

        Assert.Equal(1, page.GetProperty("items")[0].GetProperty("sequence").GetInt32());
        Assert.Equal(1, page.GetProperty("nextSequence").GetInt32());
    }

    [Fact]
    public async Task Artifact_download_returns_the_stored_bytes()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var artifact = new Artifact
        {
            Id = Guid.CreateVersion7(),
            ProjectId = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            AttemptId = Guid.NewGuid(),
            Name = "stdout.log",
            RelativePath = "test",
            Sha256 = new string('a', 64),
            SizeBytes = 5,
            MediaType = "text/plain",
            CreatedAtUtc = DateTime.UtcNow
        };
        factory.Artifacts.Artifacts[artifact.Id] = (artifact, "hello"u8.ToArray());

        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/api/v1/artifacts/{artifact.Id}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Runner_uploads_an_artifact_with_a_valid_lease()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory);
        var taskId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var token = "lease-token";
        SeedRunningTask(factory, worker, taskId, attemptId, token);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgentOS-Dev-Key", DevKey);
        client.DefaultRequestHeaders.Add("X-AgentOS-Worker-Code", worker.WorkerCode);
        client.DefaultRequestHeaders.Add("X-AgentOS-Lease-Token", token);

        using var content = new ByteArrayContent("artifact body"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        using var response = await client.PostAsync(
            $"/api/v1/runners/tasks/{taskId}/artifacts?attemptId={attemptId}&name=stdout.log&mediaType=text/plain",
            content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        Assert.Single(factory.Artifacts.Artifacts.Values);
    }

    [Fact]
    public async Task Runner_artifact_upload_rejects_an_invalid_lease()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory);
        var taskId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        SeedRunningTask(factory, worker, taskId, attemptId, "right-token");

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgentOS-Dev-Key", DevKey);
        client.DefaultRequestHeaders.Add("X-AgentOS-Worker-Code", worker.WorkerCode);
        client.DefaultRequestHeaders.Add("X-AgentOS-Lease-Token", "wrong-token");

        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes("body"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        using var response = await client.PostAsync(
            $"/api/v1/runners/tasks/{taskId}/artifacts?attemptId={attemptId}&name=x.log&mediaType=text/plain",
            content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Artifacts.Artifacts);
    }

    private static HttpClient CreateClient(AgentOSApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgentOS-Dev-Key", DevKey);
        return client;
    }

    private static TaskSummary NewSummary() => new(
        Guid.NewGuid(),
        "TASK-OBS",
        "Observability task",
        "Queued",
        "normal",
        "demo",
        "coder",
        null,
        DateTime.UtcNow,
        null,
        null,
        0,
        2);

    private static Worker SeedWorker(AgentOSApiFactory factory)
    {
        var worker = new Worker
        {
            Id = Guid.CreateVersion7(),
            WorkerCode = $"worker-{Guid.NewGuid():N}"[..20],
            DisplayName = "Runner",
            HostName = "localhost",
            Platform = "Windows",
            Architecture = "x64",
            Version = "1.0.0",
            Status = WorkerStatus.Online,
            LastHeartbeatAtUtc = DateTime.UtcNow,
            RegisteredAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        factory.Queue.Workers.Add(worker);
        return worker;
    }

    private static void SeedRunningTask(
        AgentOSApiFactory factory,
        Worker worker,
        Guid taskId,
        Guid attemptId,
        string token)
    {
        var task = new AutomationTask
        {
            Id = taskId,
            TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
            ProjectId = Guid.NewGuid(),
            AgentId = Guid.NewGuid(),
            LogicalTaskKey = $"key-{Guid.NewGuid():N}",
            Status = AgentOS.Domain.Tasks.TaskStatus.Running,
            Priority = "normal",
            Title = "artifact test",
            InputJson = "{}",
            AttemptCount = 1,
            MaxAttempts = 2,
            NextRunAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LeaseOwnerWorkerId = worker.Id,
            LeaseTokenHash = LeaseTokenService.Hash(token),
            LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        };

        factory.Queue.Tasks.Add(task);
        factory.Queue.Attempts.Add(new TaskAttempt
        {
            Id = attemptId,
            TaskId = taskId,
            WorkerId = worker.Id,
            AttemptNumber = 1,
            Status = AttemptStatus.Running,
            StartedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }
}
