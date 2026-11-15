namespace AgentOS.Domain.Projects;

/// <summary>
/// A project registered with AgentOS. AOS-002 only needs the identity and enabled flag
/// for event validation; workspace and repository details arrive with later tasks.
/// </summary>
public sealed class Project
{
    public Guid Id { get; init; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    /// <summary>One-sentence product goal; the anchor for planning.</summary>
    public string? Goal { get; set; }

    /// <summary>Project brief: background, scope, constraints and ideas.</summary>
    public string? Brief { get; set; }

    /// <summary>Free-form personal notes that should not be treated as instructions.</summary>
    public string? PersonalNotes { get; set; }

    /// <summary>What must be true for the project to be accepted.</summary>
    public string? AcceptanceCriteria { get; set; }

    /// <summary>Delivery repository in owner/name form.</summary>
    public string? Repository { get; set; }

    /// <summary>Draft, Active, OnHold, Completed or Archived.</summary>
    public string Status { get; set; } = "Active";

    /// <summary>Maximum concurrent running tasks; null means unlimited (AOS-028).</summary>
    public int? MaxConcurrentTasks { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
