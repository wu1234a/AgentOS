using AgentOS.Api.Operations;

namespace AgentOS.Api.Operations;

public sealed class DeadLetterRecoveryWorker(
    IServiceScopeFactory scopes,
    ILogger<DeadLetterRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<DeadLetterService>();
                var count = await service.AutoRecoverAsync(stoppingToken);
                if (count > 0)
                {
                    logger.LogInformation("Dead-letter auto recovery processed {Count}.", count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Dead-letter auto recovery iteration failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
