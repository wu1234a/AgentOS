namespace AgentOS.Domain.Planning;

/// <summary>Immutable snapshot of a finished planning round.</summary>
public sealed class ProjectPlanRevision
{
    public Guid Id { get; init; }

    public Guid PlanId { get; init; }

    public int Revision { get; set; }

    public string FinalPlanJson { get; set; } = string.Empty;

    public string? Feedback { get; set; }

    public string ModelProfileCode { get; set; } = string.Empty;

    public int TokensUsed { get; set; }

    public decimal EstimatedCost { get; set; }

    public DateTime CreatedAtUtc { get; init; }
}
