namespace AgentOS.Domain.Backlog;

/// <summary>
/// A pre-approved unit of work waiting for the Runner queue to become idle. Backlog items are
/// intentionally separate from tasks: dispatching one creates the normal event → task path.
/// </summary>
public sealed class BacklogItem
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Full task payload (instruction, validation commands, delivery).</summary>
    public string PayloadJson { get; set; } = "{}";

    public string Priority { get; set; } = "normal";

    /// <summary>Pending, Dispatched, Failed, or Cancelled.</summary>
    public string Status { get; set; } = "Pending";

    public Guid? EventId { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? DispatchedAtUtc { get; set; }

    public string? Error { get; set; }
}
