using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentOS.Api.Security;
using AgentOS.Domain.Events;
using AgentOS.Domain.Projects;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentOS.Api.Tests;

public sealed class EventsApiTests
{
    private const string DevKey = "test-dev-key";

    [Fact]
    public async Task Create_returns_202_and_persists_the_event()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/events", ValidEnvelope());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("accepted").GetBoolean());
        Assert.False(payload.GetProperty("duplicate").GetBoolean());
        Assert.Equal("Received", payload.GetProperty("status").GetString());
        var eventId = payload.GetProperty("eventId").GetGuid();
        Assert.NotEqual(Guid.Empty, eventId);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/events/{eventId}");
        Assert.Equal("3derp", detail.GetProperty("source").GetString());
        Assert.Equal("x", detail.GetProperty("payload").GetProperty("description").GetString());
    }

    [Fact]
    public async Task Create_missing_source_returns_400()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        var envelope = new
        {
            schemaVersion = "1.0",
            eventType = "development.task.created",
            summary = "test",
            dedupeKey = "3derp:missing-source",
            occurredAtUtc = "2026-11-05T00:00:00Z",
            payload = new { description = "x" }
        };

        using var response = await client.PostAsJsonAsync("/api/v1/events", envelope);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("source", out _));
    }

    [Fact]
    public async Task Create_non_object_payload_returns_400()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        var envelope = new
        {
            schemaVersion = "1.0",
            source = "3derp",
            eventType = "development.task.created",
            project = "3derp",
            summary = "test",
            dedupeKey = "3derp:bad-payload",
            occurredAtUtc = "2026-11-05T00:00:00Z",
            payload = new[] { 1, 2 }
        };

        using var response = await client.PostAsJsonAsync("/api/v1/events", envelope);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("payload", out _));
    }

    [Fact]
    public async Task Create_unknown_project_returns_400()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/events", ValidEnvelope(project: "missing-project"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("project", out _));
    }

    [Fact]
    public async Task Create_disabled_project_returns_400()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/events", ValidEnvelope(project: "disabled-demo"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("project", out _));
    }

    [Fact]
    public async Task Create_without_credentials_returns_401()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory, authorize: false);

        using var response = await client.PostAsJsonAsync("/api/v1/events", ValidEnvelope());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_wrong_credentials_returns_401()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgentOS-Dev-Key", "wrong-key");

        using var response = await client.PostAsJsonAsync("/api/v1/events", ValidEnvelope());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_oversized_body_returns_413()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        var json = JsonSerializer.Serialize(ValidEnvelope(summary: new string('a', 300_000)));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/events", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task List_returns_paged_and_filtered_events()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        var project = factory.Projects.Enabled;
        var receivedAtUtc = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);

        factory.Events.Seed(
            CreateEvent("3derp", "development.task.created", EventStatus.Received, receivedAtUtc, project, "a"),
            CreateEvent("3derp", "development.task.created", EventStatus.Processed, receivedAtUtc.AddMinutes(1), project, "b"),
            CreateEvent("github", "github.issue.opened", EventStatus.Received, receivedAtUtc.AddMinutes(2), project, "c"));

        using var client = CreateClient(factory);

        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/events?page=1&pageSize=2&source=3derp");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal("3derp", page.GetProperty("items")[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task List_invalid_page_size_returns_400()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/events?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_unknown_event_returns_404()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/events/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_malformed_json_returns_400()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/events", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_empty_body_returns_400()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/events", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_huge_page_returns_400()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/events?page=30000000");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Processed,Received")]
    public async Task List_ambiguous_status_returns_400(string status)
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/events?status={Uri.EscapeDataString(status)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unconfigured_authenticator_rejects_every_key()
    {
        var authenticator = new UnconfiguredApiClientAuthenticator();

        var identity = await authenticator.AuthenticateAsync("test-dev-key", CancellationToken.None);

        Assert.Null(identity);
    }

    private static HttpClient CreateClient(AgentOSApiFactory factory, bool authorize = true)
    {
        var client = factory.CreateClient();
        if (authorize)
        {
            client.DefaultRequestHeaders.Add("X-AgentOS-Dev-Key", DevKey);
        }

        return client;
    }

    private static object ValidEnvelope(
        string project = "3derp",
        string dedupeKey = "3derp:DEV-1",
        string summary = "test event") => new
    {
        schemaVersion = "1.0",
        source = "3derp",
        eventType = "development.task.created",
        project,
        summary,
        dedupeKey,
        occurredAtUtc = "2026-11-05T00:00:00Z",
        subject = new { type = "development_task", id = "DEV-1" },
        payload = new { description = "x" }
    };

    private static Event CreateEvent(
        string source,
        string eventType,
        EventStatus status,
        DateTime receivedAtUtc,
        Project project,
        string dedupeKey) => new()
    {
        Id = Guid.CreateVersion7(),
        ProjectId = project.Id,
        Project = project,
        Source = source,
        EventType = eventType,
        SchemaVersion = "1.0",
        DedupeKey = dedupeKey,
        Summary = "seeded event",
        PayloadJson = "{}",
        Priority = "normal",
        Status = status,
        OccurredAtUtc = receivedAtUtc,
        ReceivedAtUtc = receivedAtUtc
    };
}
