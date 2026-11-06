using AgentOS.Api.Tests.Fakes;
using AgentOS.Runner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class CoreRegistrationTests
{
    [Fact]
    public void Runner_services_resolve_the_task_processor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRunnerApi>(new FakeRunnerApi());
        services.AddSingleton<IOptions<RunnerOptions>>(Options.Create(new RunnerOptions()));
        services.AddRunnerServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetService<TaskProcessor>());
    }
}
