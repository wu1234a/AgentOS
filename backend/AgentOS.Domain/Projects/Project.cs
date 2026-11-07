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

    /// <summary>Maximum concurrent running tasks; null means unlimited (AOS-028).</summary>
    public int? MaxConcurrentTasks { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
