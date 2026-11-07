namespace AgentOS.Domain.Approvals;

public enum ApprovalTargetType { CodingRun = 1, PlannerDraft = 2 }

public enum ApprovalStatus { Pending = 0, Approved = 1, Rejected = 2 }

public sealed class ApprovalRequest
{
    public Guid Id { get; init; }
    public ApprovalTargetType TargetType { get; init; }
    public string TargetId { get; init; } = string.Empty;
    public ApprovalStatus Status { get; set; }
    public string? RequestReason { get; init; }
    public string RequestedByUserId { get; init; } = string.Empty;
    public DateTime RequestedAtUtc { get; init; }
    public string? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
}
