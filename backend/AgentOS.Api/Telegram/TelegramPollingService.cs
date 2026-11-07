using AgentOS.Application.Telegram;
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

        using var initialScope = scopes.CreateScope();
        var initialStore = initialScope.ServiceProvider.GetRequiredService<ITelegramOutboxStore>();
        long offset = await initialStore.GetOffsetAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var notificationScope = scopes.CreateScope())
                {
                    var store = notificationScope.ServiceProvider.GetRequiredService<ITelegramOutboxStore>();
                    var approvals = notificationScope.ServiceProvider.GetRequiredService<AgentOS.Application.Approvals.ApprovalService>();
                    var pending = await store.GetPendingAsync(20, DateTime.UtcNow, stoppingToken);
                    foreach (var notification in pending)
                    {
                        var approval = await approvals.GetAsync(notification.ApprovalId, stoppingToken);
                        if (approval is null)
                        {
                            continue;
                        }

                        try
                        {
                            await client.SendMessageAsync(
                                notification.ChatId,
                                $"待审批：{approval.TargetType} {approval.TargetId}\n理由：{approval.RequestReason ?? "-"}",
                                approval.Id.ToString("D"),
                                stoppingToken);
                            await store.MarkSentAsync(notification.Id, 0, DateTime.UtcNow, stoppingToken);
                        }
                        catch (Exception exception)
                        {
                            await store.MarkFailedAsync(
                                notification.Id,
                                exception.Message,
                                notification.AttemptCount + 1,
                                DateTime.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, notification.AttemptCount + 1))),
                                stoppingToken);
                        }
                    }
                }

                var updates = await client.GetUpdatesAsync(offset, stoppingToken);
                foreach (var update in updates)
                {
                    if (!update.TryGetProperty("update_id", out var idValue) || !idValue.TryGetInt64(out var updateId))
                    {
                        continue;
                    }

                    offset = updateId + 1;

                    try
                    {
                        using var scope = scopes.CreateScope();
                        var store = scope.ServiceProvider.GetRequiredService<ITelegramOutboxStore>();
                        if (await store.IsProcessedAsync(updateId, stoppingToken))
                        {
                            await store.SaveOffsetAsync(offset, stoppingToken);
                            continue;
                        }

                        var processor = scope.ServiceProvider.GetRequiredService<TelegramUpdateProcessor>();
                        await processor.HandleAsync(update, stoppingToken);
                        await store.RecordProcessedAsync(updateId, DateTime.UtcNow, stoppingToken);
                        await store.SaveOffsetAsync(offset, stoppingToken);
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
