using AgentOS.Application.Scheduling;

namespace AgentOS.Api.Scheduling;

public sealed class ScheduledJobWorker(
    IServiceScopeFactory scopes,
    ILogger<ScheduledJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ScheduledJobService>();
                var count = await service.RunDueAsync(stoppingToken);
                if (count > 0)
                {
                    logger.LogInformation("Scheduled jobs triggered: {Count}.", count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Scheduled job worker iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
