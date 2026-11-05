using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentOS.Api.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Live_returns_healthy()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", payload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_returns_200_when_checks_are_healthy()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", payload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_returns_503_when_checks_are_unhealthy()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Unhealthy);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unhealthy", payload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Live_returns_200_when_ready_is_unhealthy()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Unhealthy);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", payload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_failure_response_does_not_leak_configuration()
    {
        using var factory = new AgentOSApiFactory(HealthStatus.Unhealthy);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(body.Contains("Server=", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("Exception", StringComparison.OrdinalIgnoreCase));
    }
}
