namespace AgentOS.Domain.Projects;

/// <summary>
/// A formal project document generated from an approved plan. Content is Markdown and every
/// generation creates a new version instead of overwriting prior content.
/// </summary>
public sealed class ProjectDocument
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public Guid? PlanId { get; init; }

    public int PlanRevision { get; init; }

    /// <summary>PROJECT, BRAINSTORM, ROADMAP, ARCHITECTURE or ACCEPTANCE.</summary>
    public string DocumentType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Version { get; set; }

    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>Approved or Superseded.</summary>
    public string Status { get; set; } = "Approved";

    public DateTime CreatedAtUtc { get; init; }
}
