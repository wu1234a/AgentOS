using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentOS.Application.Security;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Security;
using AgentOS.Domain.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentOS.Api.Tests;

public sealed class RunnerSessionApiTests
{
    private const string DevKey = "test-dev-key";
    private const string RegistrationKey = "test-registration-key";

    [Fact]
    public async Task Registration_creates_a_worker_and_returns_a_client_secret_once()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/runners/register")
        {
            Content = JsonContent.Create(new
            {
                workerCode = "new-runner",
                displayName = "New Runner",
                hostName = "localhost",
                platform = "Windows",
                architecture = "x64",
                version = "1.0.0"
            })
        };
        request.Headers.Add("X-AgentOS-Registration-Key", RegistrationKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("clientSecret").GetString()));

        var worker = Assert.Single(factory.Queue.Workers);
        Assert.Equal("new-runner", worker.WorkerCode);
        Assert.Equal(WorkerStatus.Online, worker.Status);

        var apiClient = Assert.Single(factory.ApiClients.Clients);
        Assert.Equal("runner-new-runner", apiClient.ClientCode);
        Assert.Equal(worker.Id, apiClient.WorkerId);
        Assert.True(apiClient.IsEnabled);
        Assert.NotEqual(body.GetProperty("clientSecret").GetString(), apiClient.SecretHash);
    }

    [Fact]
    public async Task Registration_rejects_a_wrong_key()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/runners/register")
        {
            Content = JsonContent.Create(new
            {
                workerCode = "new-runner",
                displayName = "New Runner",
                hostName = "localhost"
            })
        };
        request.Headers.Add("X-AgentOS-Registration-Key", "wrong-key");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.ApiClients.Clients);
    }

    [Fact]
    public async Task Registered_client_credentials_authenticate_runner_endpoints()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory);
        var secret = ClientSecretHasher.CreateSecret();
        factory.ApiClients.Clients.Add(new ApiClient
        {
            Id = Guid.NewGuid(),
            ClientCode = "runner-client",
            ClientType = ApiClientType.Runner,
            WorkerId = worker.Id,
            SecretHash = ClientSecretHasher.Hash(secret),
            ScopesJson = "[]",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgentOS-Client-Id", "runner-client");
        client.DefaultRequestHeaders.Add("X-AgentOS-Client-Secret", secret);

        using var response = await client.PostAsJsonAsync("/api/v1/runners/claim", new { capacity = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Client_credentials_reject_a_disabled_worker()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory);
        worker.Status = WorkerStatus.Disabled;

        var secret = "disabled-secret";
        factory.ApiClients.Clients.Add(new ApiClient
        {
            Id = Guid.NewGuid(),
            ClientCode = "disabled-client",
            ClientType = ApiClientType.Runner,
            WorkerId = worker.Id,
            SecretHash = ClientSecretHasher.Hash(secret),
            ScopesJson = "[]",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgentOS-Client-Id", "disabled-client");
        client.DefaultRequestHeaders.Add("X-AgentOS-Client-Secret", secret);

        using var response = await client.PostAsJsonAsync("/api/v1/runners/claim", new { capacity = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Claimer.Calls);
    }

    [Fact]
    public async Task Invalid_client_secret_is_rejected()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory);
        factory.ApiClients.Clients.Add(new ApiClient
        {
            Id = Guid.NewGuid(),
            ClientCode = "runner-client",
            ClientType = ApiClientType.Runner,
            WorkerId = worker.Id,
            SecretHash = ClientSecretHasher.Hash("right-secret"),
            ScopesJson = "[]",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgentOS-Client-Id", "runner-client");
        client.DefaultRequestHeaders.Add("X-AgentOS-Client-Secret", "wrong-secret");

        using var response = await client.PostAsJsonAsync("/api/v1/runners/claim", new { capacity = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Worker_heartbeat_updates_the_last_seen_time()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory);
        worker.LastHeartbeatAtUtc = DateTime.UtcNow.AddMinutes(-10);

        using var client = CreateDevClient(factory, worker.WorkerCode);
        using var response = await client.PostAsync("/api/v1/runners/heartbeat", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(worker.LastHeartbeatAtUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Logs_are_stored_for_a_valid_lease()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory);
        var task = SeedRunningTask(factory, worker);

        using var client = CreateDevClient(factory, worker.WorkerCode);
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/runners/tasks/{task.TaskId}/logs",
            new
            {
                attemptId = task.AttemptId,
                leaseToken = task.Token,
                entries = new[]
                {
                    new { sequence = 1, level = "info", message = "starting" },
                    new { sequence = 2, level = "warn", message = "slow" }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, factory.Logs.Entries.Count);
        Assert.Equal("starting", factory.Logs.Entries[0].Message);
    }

    [Fact]
    public async Task Logs_require_a_valid_lease()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var worker = SeedWorker(factory);
        var task = SeedRunningTask(factory, worker);

        using var client = CreateDevClient(factory, worker.WorkerCode);
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/runners/tasks/{task.TaskId}/logs",
            new
            {
                attemptId = task.AttemptId,
                leaseToken = "wrong-token",
                entries = new[] { new { sequence = 1, level = "info", message = "hello" } }
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Logs.Entries);
    }

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

    private static (Guid TaskId, Guid AttemptId, string Token) SeedRunningTask(
        AgentOSApiFactory factory,
        Worker worker)
    {
        var token = "lease-token";
        var task = new AgentOS.Domain.Tasks.AutomationTask
        {
            Id = Guid.CreateVersion7(),
            TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
            ProjectId = Guid.NewGuid(),
            AgentId = Guid.NewGuid(),
            LogicalTaskKey = $"key-{Guid.NewGuid():N}",
            Status = AgentOS.Domain.Tasks.TaskStatus.Running,
            Priority = "normal",
            Title = "logs test",
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

        var attempt = new AgentOS.Domain.Tasks.TaskAttempt
        {
            Id = Guid.CreateVersion7(),
            TaskId = task.Id,
            WorkerId = worker.Id,
            AttemptNumber = 1,
            Status = AgentOS.Domain.Tasks.AttemptStatus.Running,
            StartedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        factory.Queue.Tasks.Add(task);
        factory.Queue.Attempts.Add(attempt);
        return (task.Id, attempt.Id, token);
    }

    private static HttpClient CreateDevClient(AgentOSApiFactory factory, string workerCode)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgentOS-Dev-Key", DevKey);
        client.DefaultRequestHeaders.Add("X-AgentOS-Worker-Code", workerCode);
        return client;
    }
}
