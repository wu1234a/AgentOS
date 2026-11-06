using AgentOS.Application.Tasks;
using Microsoft.Extensions.Options;

namespace AgentOS.Worker;

/// <summary>
/// Periodically recovers tasks whose lease expired. Runs on a fixed interval and never lets a
/// failed pass kill the host.
/// </summary>
public sealed class LeaseRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TaskQueueOptions> options,
    ILogger<LeaseRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.RecoveryIntervalSeconds));
        logger.LogInformation("Lease recovery worker started with a {IntervalSeconds}s interval.", interval.TotalSeconds);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var recovery = scope.ServiceProvider.GetRequiredService<LeaseRecoveryService>();
                await recovery.RecoverAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A transient database failure must not stop future recovery passes.
                logger.LogError(exception, "Lease recovery pass failed; it will retry on the next interval.");
            }
        }
    }
}
