using Microsoft.Extensions.Options;

namespace AgentOS.Api.Telegram;

public sealed class TelegramPollingService(
    ITelegramClient client,
    IServiceScopeFactory scopes,
    IOptions<TelegramOptions> options,
    ILogger<TelegramPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled || string.IsNullOrWhiteSpace(configuration.BotToken))
        {
            logger.LogInformation("Telegram bot is disabled.");
            return;
        }

        if (configuration.AllowedChatIds.Length == 0 || configuration.AllowedUserIds.Length == 0)
        {
            logger.LogError("Telegram bot requires AllowedChatIds and AllowedUserIds; not starting.");
            return;
        }

        long offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await client.GetUpdatesAsync(offset, stoppingToken);
                foreach (var update in updates)
                {
                    if (update.TryGetProperty("update_id", out var idValue) && idValue.TryGetInt64(out var updateId))
                    {
                        offset = updateId + 1;
                    }

                    try
                    {
                        using var scope = scopes.CreateScope();
                        var processor = scope.ServiceProvider.GetRequiredService<TelegramUpdateProcessor>();
                        await processor.HandleAsync(update, stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // A permanently failing update (for example an expired callback) must not
                        // block every later update; it has already been offset past.
                        logger.LogWarning(exception, "Telegram update could not be processed; skipping.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Telegram polling failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
