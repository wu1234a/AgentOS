using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Events;
using AgentOS.Application.Projects;
using AgentOS.Application.Rules;
using AgentOS.Application.Observability;
using AgentOS.Application.Runners;
using AgentOS.Application.Security;
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

    public FakeApiClientStore ApiClients { get; } = new();

    public FakeTaskLogStore Logs { get; } = new();

    public FakeRunnerRegistrationStore RunnerRegistration => new(Queue, ApiClients);

    public FakeObservabilityQueryStore Observability { get; } = new();

    public FakeArtifactStore Artifacts { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:DevelopmentApiKey"] = "test-dev-key",
                ["Runner:RegistrationKey"] = "test-registration-key",
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
            services.RemoveAll<ITaskLogStore>();
            services.RemoveAll<IApiClientStore>();
            services.RemoveAll<IRunnerRegistrationStore>();
            services.RemoveAll<IObservabilityQueryStore>();
            services.RemoveAll<IArtifactStore>();
            services.AddSingleton<IEventRepository>(Events);
            services.AddSingleton<IProjectLookup>(Projects);
            services.AddSingleton<IAutomationRuleRepository>(Rules);
            services.AddSingleton<IRuleReferenceLookup>(RuleReferences);
            services.AddSingleton<ITaskDispatcher>(Dispatcher);
            services.AddSingleton<ITaskQueueStore>(Queue);
            services.AddSingleton<ITaskClaimer>(Claimer);
            services.AddSingleton<ITaskLogStore>(Logs);
            services.AddSingleton<IApiClientStore>(ApiClients);
            services.AddSingleton<IRunnerRegistrationStore>(RunnerRegistration);
            services.AddSingleton<IObservabilityQueryStore>(Observability);
            services.AddSingleton<IArtifactStore>(Artifacts);

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
