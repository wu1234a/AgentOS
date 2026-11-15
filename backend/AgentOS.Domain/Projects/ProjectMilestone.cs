namespace AgentOS.Domain.Projects;

/// <summary>A roadmap milestone that must be verified by a human before project acceptance.</summary>
public sealed class ProjectMilestone
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public Guid PlanId { get; init; }

    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Goal { get; set; } = string.Empty;

    public string ExitCriteria { get; set; } = string.Empty;

    /// <summary>Pending or Verified.</summary>
    public string Status { get; set; } = "Pending";

    public DateTime? VerifiedAtUtc { get; set; }

    public string? VerifiedBy { get; set; }

    public DateTime CreatedAtUtc { get; init; }
}
