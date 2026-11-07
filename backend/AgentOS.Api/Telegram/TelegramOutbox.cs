using AgentOS.Application.Telegram;
using AgentOS.Domain.Telegram;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Telegram;

public sealed class TelegramOutbox(
    AgentOSDbContext dbContext,
    IOptions<TelegramOptions> options,
    TimeProvider timeProvider) : ITelegramOutbox, ITelegramOutboxStore
{
    public async Task EnqueueAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled || configuration.NotificationChatId == 0)
        {
            return;
        }

        if (await dbContext.TelegramNotifications.AnyAsync(
                notification => notification.ApprovalId == approvalId,
                cancellationToken))
        {
            return;
        }

        dbContext.TelegramNotifications.Add(new TelegramNotification
        {
            Id = Guid.CreateVersion7(),
            ApprovalId = approvalId,
            ChatId = configuration.NotificationChatId,
            Status = "Pending",
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TelegramNotification>> GetPendingAsync(
        int limit,
        DateTime nowUtc,
        CancellationToken cancellationToken)
        => await dbContext.TelegramNotifications
            .Where(notification => notification.Status == "Pending"
                && (notification.NextRetryAtUtc == null || notification.NextRetryAtUtc <= nowUtc))
            .OrderBy(notification => notification.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task MarkSentAsync(Guid id, long messageId, DateTime sentAtUtc, CancellationToken cancellationToken)
    {
        var notification = await dbContext.TelegramNotifications.FirstOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (notification is null)
        {
            return;
        }

        notification.Status = "Sent";
        notification.MessageId = messageId;
        notification.SentAtUtc = sentAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid id,
        string error,
        int attempt,
        DateTime? nextRetryAtUtc,
        CancellationToken cancellationToken)
    {
        var notification = await dbContext.TelegramNotifications.FirstOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (notification is null)
        {
            return;
        }

        notification.AttemptCount = attempt;
        notification.LastError = error.Length <= 1000 ? error : error[..1000];
        notification.NextRetryAtUtc = nextRetryAtUtc;
        notification.Status = nextRetryAtUtc is null || attempt >= options.Value.MaxSendAttempts
            ? "Failed"
            : "Pending";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<long> GetOffsetAsync(CancellationToken cancellationToken)
        => await dbContext.TelegramPollerState
            .Where(state => state.Id == 1)
            .Select(state => (long?)state.NextOffset)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

    public async Task SaveOffsetAsync(long offset, CancellationToken cancellationToken)
    {
        var state = await dbContext.TelegramPollerState.FirstOrDefaultAsync(candidate => candidate.Id == 1, cancellationToken);
        if (state is null)
        {
            dbContext.TelegramPollerState.Add(new TelegramPollerState { Id = 1, NextOffset = offset });
        }
        else
        {
            state.NextOffset = offset;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> IsProcessedAsync(long updateId, CancellationToken cancellationToken)
        => dbContext.TelegramProcessedUpdates.AnyAsync(update => update.UpdateId == updateId, cancellationToken);

    public async Task RecordProcessedAsync(long updateId, DateTime processedAtUtc, CancellationToken cancellationToken)
    {
        dbContext.TelegramProcessedUpdates.Add(new TelegramProcessedUpdate
        {
            UpdateId = updateId,
            ProcessedAtUtc = processedAtUtc
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
