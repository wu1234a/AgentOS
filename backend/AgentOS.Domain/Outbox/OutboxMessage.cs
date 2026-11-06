namespace AgentOS.Domain.Outbox;

public enum OutboxStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3
}

/// <summary>Transactional outbox used by later tasks for reliable side effects.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    public string Type { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public OutboxStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTime AvailableAtUtc { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; set; }
}
