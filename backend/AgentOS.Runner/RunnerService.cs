using AgentOS.Application.Tasks;
using Microsoft.Extensions.Options;

namespace AgentOS.Runner;

/// <summary>
/// Long-running runner loop: register, poll for work, run up to MaxConcurrentTasks handlers,
/// keep the worker heartbeat alive, and back off when the control plane is unreachable.
/// </summary>
public sealed class RunnerService(
    IServiceScopeFactory scopeFactory,
    IRunnerApi api,
    IOptions<RunnerOptions> options,
    ILogger<RunnerService> logger) : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var errors = options.Value.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid runner configuration: {string.Join(" ", errors)}");
        }

        await EnsureRegisteredAsync(stoppingToken);

        logger.LogInformation(
            "Runner {WorkerCode} started against {BaseUrl} (max {Concurrency} concurrent task(s), poll {Poll}s).",
            options.Value.WorkerCode,
            options.Value.BaseUrl,
            options.Value.MaxConcurrentTasks,
            options.Value.PollIntervalSeconds);

        var workerHeartbeat = WorkerHeartbeatLoopAsync(stoppingToken);
        var running = new List<Task>();
        var backoff = TimeSpan.FromSeconds(2);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                running.RemoveAll(task => task.IsCompleted);

                var capacity = options.Value.MaxConcurrentTasks - running.Count;

                if (capacity > 0)
                {
                    try
                    {
                        var claimed = await api.ClaimAsync(capacity, stoppingToken);
                        backoff = TimeSpan.FromSeconds(2);

                        foreach (var task in claimed)
                        {
                            running.Add(RunTaskAsync(task, stoppingToken));
                        }
                    }
                    catch (RunnerApiException exception) when (exception.IsTransient)
                    {
                        logger.LogWarning(
                            "Claim failed ({Code}); backing off for {Delay}s while the API is unavailable.",
                            exception.Code,
                            backoff.TotalSeconds);
                        await Task.Delay(backoff, stoppingToken);
                        backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
                        continue;
                    }
                    catch (RunnerApiException exception)
                    {
                        logger.LogError(
                            "Claim was rejected ({Status}): {Code}. Check the runner credentials.",
                            exception.StatusCode,
                            exception.Code);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            logger.LogInformation("Runner stopping; waiting up to {Grace}s for {Count} running task(s).",
                ShutdownGrace.TotalSeconds,
                running.Count);

            await Task.WhenAny(Task.WhenAll(running), Task.Delay(ShutdownGrace));

            try
            {
                await workerHeartbeat;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }
    }

    private async Task RunTaskAsync(ClaimedTask task, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<TaskProcessor>();

        try
        {
            await processor.ProcessAsync(task, stoppingToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled error while processing task {TaskId}.", task.TaskId);
        }
    }

    private async Task EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.ClientId)
            && !string.IsNullOrWhiteSpace(options.Value.ClientSecret))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Value.RegistrationKey))
        {
            if (!string.IsNullOrWhiteSpace(options.Value.DevApiKey))
            {
                logger.LogWarning(
                    "No runner client credentials configured; falling back to the development API key.");
                return;
            }

            throw new InvalidOperationException(
                "Configure RegistrationKey (one-time bootstrap) or ClientId/ClientSecret (or DevApiKey for development).");
        }

        var registration = await api.RegisterAsync(options.Value.RegistrationKey, cancellationToken);
        options.Value.ClientId = registration.ClientId;
        options.Value.ClientSecret = registration.ClientSecret;

        logger.LogInformation(
            "Registered worker {WorkerId} as client {ClientId}; the bootstrap key is no longer needed.",
            registration.WorkerId,
            registration.ClientId);
    }

    private async Task WorkerHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.HeartbeatIntervalSeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);
                await api.HeartbeatWorkerAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Worker heartbeat failed; the runner stays online but may look stale.");
            }
        }
    }
}
