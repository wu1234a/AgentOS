namespace AgentOS.Worker;

/// <summary>
/// AOS-001 placeholder: the worker process starts and stays alive, but no lease
/// recovery, outbox, or retention jobs are registered yet (AOS-005 and later).
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AgentOS.Worker started. No jobs are registered in AOS-001.");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("AgentOS.Worker is shutting down.");
        }
    }
}
