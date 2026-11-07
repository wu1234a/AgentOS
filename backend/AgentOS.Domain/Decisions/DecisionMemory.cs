namespace AgentOS.Domain.Decisions;

public sealed class DecisionMemory
{
    public Guid Id { get; init; }
    public Guid SourceApprovalRequestId { get; init; }
    public string ScopeType { get; init; } = string.Empty;
    public string ScopeKey { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string? Reason { get; set; }
    public string DecidedBy { get; init; } = string.Empty;
    public DateTime DecidedAtUtc { get; init; }
    public DateTime ValidFromUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? InvalidatedAtUtc { get; set; }
    public string? InvalidatedBy { get; set; }
    public string? InvalidationReason { get; set; }
    public int HitCount { get; set; }
    public DateTime? LastHitAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
