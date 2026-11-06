using AgentOS.Runner.Handlers;

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
        services.AddSingleton<ITaskHandler, DemoTaskHandler>();
        services.AddTransient<TaskProcessor>();
        return services;
    }
}
