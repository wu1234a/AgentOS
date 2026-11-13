namespace AgentOS.Domain.Planning;

/// <summary>
/// A project-level plan produced by a small multi-agent planning board. The plan remains
/// PendingReview until a human approves it; approval is what turns its task list into Backlog.
/// </summary>
public sealed class ProjectPlan
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public string Title { get; set; } = string.Empty;

    public string Goal { get; set; } = string.Empty;

    public string? Constraints { get; set; }

    /// <summary>Serialized delivery settings used when approved tasks become Backlog items.</summary>
    public string DeliveryJson { get; set; } = "{}";

    public string ModelProfileCode { get; set; } = string.Empty;

    public string Status { get; set; } = "Queued";

    public string? ProposalsJson { get; set; }

    public string? CritiquesJson { get; set; }

    public string? FinalPlanJson { get; set; }

    public string? Error { get; set; }

    /// <summary>Revision number; incremented when a human asks the board to re-plan.</summary>
    public int Revision { get; set; } = 1;

    /// <summary>Latest human feedback used by the next planning round.</summary>
    public string? Feedback { get; set; }

    public int TokenBudget { get; set; } = 60_000;

    public int TokensUsed { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public decimal EstimatedCost { get; set; }

    public int ModelCalls { get; set; }

    /// <summary>Final plan from the previous revision, retained for comparison.</summary>
    public string? PreviousFinalPlanJson { get; set; }

    public int? PreviousRevision { get; set; }

    /// <summary>When true, release requires a second explicit human approval.</summary>
    public bool RequiresReleaseApproval { get; set; } = true;

    public DateTime? ReleaseApprovedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? DecidedAtUtc { get; set; }
}

public sealed class ProjectPlanContribution
{
    public Guid Id { get; init; }

    public Guid PlanId { get; init; }

    public string Role { get; set; } = string.Empty;

    public int Round { get; set; }

    public string Content { get; set; } = string.Empty;

    public string ModelProfileCode { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }
}
