using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentOS.Api.Tests;

/// <summary>
/// Boots the real application pipeline and replaces the SQL Server readiness check
/// with a deterministic stub, so endpoint behavior is tested without a database.
/// </summary>
public sealed class AgentOSApiFactory(HealthStatus healthStatus) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration(
                    name: "stub",
                    factory: _ => new StubHealthCheck(healthStatus),
                    failureStatus: null,
                    tags: ["ready"]));
            });
        });
    }

    private sealed class StubHealthCheck(HealthStatus status) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new HealthCheckResult(status));
    }
}
