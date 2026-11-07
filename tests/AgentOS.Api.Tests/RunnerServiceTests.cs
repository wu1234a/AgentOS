using AgentOS.Application.Tasks;
using AgentOS.Api.Tests.Fakes;
using AgentOS.Runner;
using AgentOS.Runner.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class RunnerServiceTests
{
    private readonly string _root;
    private readonly string _workspace;

    public RunnerServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentos-runner-loop", Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "demo");
        Directory.CreateDirectory(_workspace);
    }

    [Fact]
    public async Task Runner_backs_off_then_claims_and_completes_a_task()
    {
        // First poll fails like an unreachable API; the loop must retry instead of exiting.
        var api = new FakeRunnerApi { ClaimFailuresBeforeSuccess = 1 };

        var task = NewTask();
        api.Claimed.Add(task);

        var options = new RunnerOptions
        {
            BaseUrl = "http://localhost",
            WorkerCode = "test-runner",
            RegistrationKey = "bootstrap",
            WorkspaceRoots = [_root],
            Projects = [new ProjectWorkspace { Slug = "demo", Path = _workspace }],
            PollIntervalSeconds = 1,
            HeartbeatIntervalSeconds = 30,
            MaxConcurrentTasks = 1
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRunnerApi>(api);
        services.AddSingleton<IOptions<RunnerOptions>>(Options.Create(options));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WorkspacePolicy>();
        services.AddSingleton<ITaskHandler, FakeTaskHandler>();
        services.AddSingleton<IWorktreeManager>(new NoOpWorktreeManager());
        services.AddTransient<TaskProcessor>();

        await using var provider = services.BuildServiceProvider();
        var runner = new RunnerService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            api,
            Options.Create(options),
            NullLogger<RunnerService>.Instance);

        await runner.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => api.Completed.Count > 0, TimeSpan.FromSeconds(12));
        await runner.StopAsync(CancellationToken.None);

        Assert.Contains(task.TaskId, api.Completed);
        Assert.Empty(api.Failed);
        Assert.True(api.Started.Count > 0);
    }

    [Fact]
    public async Task Runner_retries_a_transient_registration_failure()
    {
        var api = new FakeRunnerApi { RegisterFailuresBeforeSuccess = 1 };
        var task = NewTask();
        api.Claimed.Add(task);

        var options = new RunnerOptions
        {
            BaseUrl = "http://localhost",
            WorkerCode = "test-runner",
            RegistrationKey = "bootstrap",
            WorkspaceRoots = [_root],
            Projects = [new ProjectWorkspace { Slug = "demo", Path = _workspace }],
            PollIntervalSeconds = 1,
            HeartbeatIntervalSeconds = 30,
            MaxConcurrentTasks = 1
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRunnerApi>(api);
        services.AddSingleton<IOptions<RunnerOptions>>(Options.Create(options));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WorkspacePolicy>();
        services.AddSingleton<ITaskHandler, FakeTaskHandler>();
        services.AddSingleton<IWorktreeManager>(new NoOpWorktreeManager());
        services.AddTransient<TaskProcessor>();

        await using var provider = services.BuildServiceProvider();
        var runner = new RunnerService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            api,
            Options.Create(options),
            NullLogger<RunnerService>.Instance);

        await runner.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => api.Completed.Count > 0, TimeSpan.FromSeconds(20));
        await runner.StopAsync(CancellationToken.None);

        Assert.Contains(task.TaskId, api.Completed);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }
    }

    private static ClaimedTask NewTask() => new(
        Guid.NewGuid(),
        "demo",
        "TASK-LOOP",
        "Runner loop test",
        "normal",
        "{}",
        "agent-code",
        "coder",
        null,
        null,
        null,
        null,
        Guid.NewGuid(),
        null,
        null,
        2,
        1800,
        new TaskLease(Guid.NewGuid(), 1, "lease-token", DateTime.UtcNow.AddMinutes(5)));
}
