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
        services.AddSingleton<CodexCliAdapter>();
        // Model calls are bounded by the profile/task timeout, not HttpClient's 100s default.
        services.AddSingleton(_ => new OpenAiCompatibleAgentAdapter(
            new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan }));
        services.AddSingleton(_ => new AnthropicAgentAdapter(
            new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan }));
        services.AddSingleton<IAgentAdapter>(provider => new RoutingAgentAdapter(
            provider.GetRequiredService<CodexCliAdapter>(),
            provider.GetRequiredService<OpenAiCompatibleAgentAdapter>(),
            provider.GetRequiredService<AnthropicAgentAdapter>()));
        services.AddSingleton<IValidationCommandRunner, ValidationCommandRunner>();
        services.AddSingleton<IChangeSetCapture, GitChangeSetCapture>();
        services.AddSingleton<IWorktreeManager, GitWorktreeManager>();
        services.AddSingleton<DemoTaskHandler>();
        services.AddSingleton<ITaskHandler>(provider =>
        {
            var adapterCode = provider.GetRequiredService<IOptions<RunnerOptions>>().Value.AgentAdapter;

            if (string.Equals(adapterCode, "codex-cli", StringComparison.OrdinalIgnoreCase)
                || string.Equals(adapterCode, "openai-compatible", StringComparison.OrdinalIgnoreCase)
                || string.Equals(adapterCode, "multi", StringComparison.OrdinalIgnoreCase))
            {
                return new CodexTaskHandler(
                    provider.GetRequiredService<IAgentAdapter>(),
                    provider.GetRequiredService<IValidationCommandRunner>(),
                    provider.GetRequiredService<IChangeSetCapture>(),
                    provider.GetRequiredService<IOptions<CodexCliOptions>>(),
                    provider.GetRequiredService<ILogger<CodexTaskHandler>>(),
                    allowAnyProvider: true);
            }

            return provider.GetRequiredService<DemoTaskHandler>();
        });
        services.AddTransient<TaskProcessor>();
        return services;
    }
}
