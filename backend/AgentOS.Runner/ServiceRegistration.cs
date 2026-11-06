using AgentOS.AgentAdapters;
using AgentOS.AgentAdapters.Codex;
using AgentOS.AgentAdapters.Processes;
using AgentOS.Runner.Handlers;
using Microsoft.Extensions.Options;

namespace AgentOS.Runner;

/// <summary>
/// Registers the runner's host services. Shared by Program and the DI smoke test so the real
/// executable and the test cannot drift apart (a missing TimeProvider once broke every task).
/// </summary>
public static class RunnerServiceRegistration
{
    public static IServiceCollection AddRunnerServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WorkspacePolicy>();
        services.AddOptions<CodexCliOptions>();
        services.AddSingleton<IProcessRunner, ExternalProcessRunner>();
        services.AddSingleton<IAgentAdapter, CodexCliAdapter>();
        services.AddSingleton<DemoTaskHandler>();
        services.AddSingleton<ITaskHandler>(provider =>
        {
            var adapterCode = provider.GetRequiredService<IOptions<RunnerOptions>>().Value.AgentAdapter;

            if (string.Equals(adapterCode, "codex-cli", StringComparison.OrdinalIgnoreCase))
            {
                return new CodexTaskHandler(
                    provider.GetRequiredService<IAgentAdapter>(),
                    provider.GetRequiredService<IOptions<CodexCliOptions>>(),
                    provider.GetRequiredService<ILogger<CodexTaskHandler>>());
            }

            return provider.GetRequiredService<DemoTaskHandler>();
        });
        services.AddTransient<TaskProcessor>();
        return services;
    }
}
