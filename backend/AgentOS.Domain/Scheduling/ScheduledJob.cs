namespace AgentOS.Domain.Scheduling;

public sealed class ScheduledJob
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public int IntervalSeconds { get; set; }
    public DateTime FirstRunAtUtc { get; init; }
    public DateTime NextRunAtUtc { get; set; }
    public bool Enabled { get; set; }
    public DateTime? LastTriggeredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
