using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Events;
using AgentOS.Application.Projects;
using AgentOS.Application.Rules;
using AgentOS.Application.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentOS.Api.Tests;

/// <summary>
/// Boots the real application pipeline and replaces infrastructure seams with
/// deterministic fakes, so endpoint behavior is tested without a database.
/// </summary>
public sealed class AgentOSApiFactory(HealthStatus healthStatus, string environment = "Development")
    : WebApplicationFactory<Program>
{
    private FakeAutomationRuleRepository? _rules;

    public FakeEventRepository Events { get; } = new();

    public FakeProjectLookup Projects { get; } = new();

    public FakeRuleReferenceLookup RuleReferences { get; } = FakeRuleReferenceLookup.Default();

    public FakeAutomationRuleRepository Rules => _rules ??= new FakeAutomationRuleRepository(RuleReferences);

    public FakeTaskDispatcher Dispatcher { get; } = new();

    public FakeTaskQueueStore Queue { get; } = new();

    public FakeTaskClaimer Claimer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:DevelopmentApiKey"] = "test-dev-key",
                ["ConnectionStrings:AgentOS"] =
                    "Server=.;Database=AgentOS_Dev;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventRepository>();
            services.RemoveAll<IProjectLookup>();
            services.RemoveAll<IAutomationRuleRepository>();
            services.RemoveAll<IRuleReferenceLookup>();
            services.RemoveAll<ITaskDispatcher>();
            services.RemoveAll<ITaskQueueStore>();
            services.RemoveAll<ITaskClaimer>();
            services.AddSingleton<IEventRepository>(Events);
            services.AddSingleton<IProjectLookup>(Projects);
            services.AddSingleton<IAutomationRuleRepository>(Rules);
            services.AddSingleton<IRuleReferenceLookup>(RuleReferences);
            services.AddSingleton<ITaskDispatcher>(Dispatcher);
            services.AddSingleton<ITaskQueueStore>(Queue);
            services.AddSingleton<ITaskClaimer>(Claimer);

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
