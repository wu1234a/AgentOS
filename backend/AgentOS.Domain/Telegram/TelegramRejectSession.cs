namespace AgentOS.Domain.Telegram;

public sealed class TelegramRejectSession
{
    public Guid Id { get; init; }
    public Guid ApprovalId { get; init; }
    public long ChatId { get; init; }
    public long UserId { get; init; }
    public long MessageId { get; init; }
    public string Status { get; set; } = "AwaitingReason";
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
