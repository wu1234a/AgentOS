using AgentOS.Domain.Telegram;

namespace AgentOS.Application.Telegram;

public interface ITelegramOutbox
{
    Task EnqueueAsync(Guid approvalId, CancellationToken cancellationToken);
}

public interface ITelegramOutboxStore
{
    Task<IReadOnlyList<TelegramNotification>> GetPendingAsync(int limit, DateTime nowUtc, CancellationToken cancellationToken);
    Task MarkSentAsync(Guid id, long messageId, DateTime sentAtUtc, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid id, string error, int attempt, DateTime? nextRetryAtUtc, CancellationToken cancellationToken);
    Task<long> GetOffsetAsync(CancellationToken cancellationToken);
    Task SaveOffsetAsync(long offset, CancellationToken cancellationToken);
    Task<bool> IsProcessedAsync(long updateId, CancellationToken cancellationToken);
    Task RecordProcessedAsync(long updateId, DateTime processedAtUtc, CancellationToken cancellationToken);
}
