namespace AgentOS.Domain.Telegram;

public sealed class TelegramNotification
{
    public Guid Id { get; init; }
    public Guid ApprovalId { get; init; }
    public long ChatId { get; init; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime? NextRetryAtUtc { get; set; }
    public string? LastError { get; set; }
    public long? MessageId { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? SentAtUtc { get; set; }
}

public sealed class TelegramPollerState
{
    public int Id { get; init; } = 1;
    public long NextOffset { get; set; }
}

public sealed class TelegramProcessedUpdate
{
    public long UpdateId { get; init; }
    public DateTime ProcessedAtUtc { get; init; }
}
