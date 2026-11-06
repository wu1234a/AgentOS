using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentOS.Api.Tests;

public sealed class RulesApiTests
{
    private const string DevKey = "test-dev-key";

    [Fact]
    public async Task Create_returns_201_and_the_normalized_rule()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/rules", ValidRule());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal("3derp", body.GetProperty("projectSlug").GetString());
        Assert.Equal("planner", body.GetProperty("agentCode").GetString());
        Assert.Equal(1, body.GetProperty("version").GetInt32());
        Assert.True(body.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task Create_with_unknown_agent_returns_400_with_field_error()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/rules",
            ValidRule(agentCode: "missing-agent"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("agentCode", out _));
    }

    [Fact]
    public async Task Create_with_invalid_match_json_returns_400()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/rules",
            ValidRule(matchJson: new { unknownCondition = true }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("matchJson", out _));
    }

    [Fact]
    public async Task List_and_get_return_the_created_rule()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        var created = await CreateRuleAsync(client);

        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/rules");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal("3derp-dev-plan", page.GetProperty("items")[0].GetProperty("name").GetString());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rules/{created}");
        Assert.Equal("3derp", detail.GetProperty("projectSlug").GetString());
        Assert.Equal("urgent", detail.GetProperty("dispatchJson").GetProperty("priority").GetString());
    }

    [Fact]
    public async Task Get_unknown_rule_returns_404()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/rules/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_bumps_the_version()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        var id = await CreateRuleAsync(client);

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/rules/{id}",
            ValidRule(name: "renamed rule", priority: 20));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("renamed rule", body.GetProperty("name").GetString());
        Assert.Equal(20, body.GetProperty("priority").GetInt32());
        Assert.Equal(2, body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Enable_is_rejected_when_the_agent_is_disabled()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var create = await client.PostAsJsonAsync(
            "/api/v1/rules",
            ValidRule(agentCode: "disabled-agent", isEnabled: false));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var enable = await client.PostAsync($"/api/v1/rules/{id}/enable", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, enable.StatusCode);

        var problem = await enable.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("agentCode", out _));

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rules/{id}");
        Assert.False(detail.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task Disable_returns_the_disabled_rule()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        var id = await CreateRuleAsync(client);

        using var response = await client.PostAsync($"/api/v1/rules/{id}/disable", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task Enable_and_disable_unknown_rule_return_404()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/rules/{id}/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/rules/{id}/disable", null)).StatusCode);
    }

    [Fact]
    public async Task Validate_reports_valid_and_invalid_rules_without_saving()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var valid = await client.PostAsJsonAsync(
            "/api/v1/rules/validate",
            ValidRule(agentCode: "disabled-agent", isEnabled: false));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        var validBody = await valid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(validBody.GetProperty("isValid").GetBoolean());

        using var invalid = await client.PostAsJsonAsync(
            "/api/v1/rules/validate",
            ValidRule(sourcePattern: "bad?pattern"));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(invalidBody.GetProperty("isValid").GetBoolean());
        Assert.True(invalidBody.GetProperty("errors").TryGetProperty("sourcePattern", out _));

        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/rules");
        Assert.Equal(0, page.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task List_rejects_invalid_page_size()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/rules?pageSize=500");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rules_require_authentication()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = CreateClient(factory, authorize: false);

        using var response = await client.GetAsync("/api/v1/rules");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Guid> CreateRuleAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/rules", ValidRule());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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

    private static object ValidRule(
        string name = "3derp-dev-plan",
        string agentCode = "planner",
        string sourcePattern = "3derp",
        object? matchJson = null,
        bool isEnabled = true,
        int priority = 10)
        => new
        {
            name,
            project = "3derp",
            sourcePattern,
            eventTypePattern = "development.task.*",
            agentCode,
            skillCode = "3derp-plan-development",
            modelProfileCode = "strong-reasoning",
            matchJson = matchJson ?? new { subjectTypes = new[] { "development_task" } },
            dispatchJson = new { dispatchKey = "default", priority = "urgent" },
            priority,
            maxParallel = 1,
            maxAttempts = 2,
            timeoutSeconds = 1800,
            notificationPolicy = "failure-or-approval",
            isEnabled
        };
}
