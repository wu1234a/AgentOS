namespace AgentOS.Api.Telegram;

public sealed class TelegramOptions
{
    public bool Enabled { get; set; }
    public string? BotToken { get; set; }
    public long[] AllowedChatIds { get; set; } = [];
    public long[] AllowedUserIds { get; set; } = [];
    public int PollTimeoutSeconds { get; set; } = 30;
    public long NotificationChatId { get; set; }
    public int MaxSendAttempts { get; set; } = 5;
}
