namespace AgentOS.Domain.Skills;

/// <summary>A versioned SOP file that a rule can attach to a task.</summary>
public sealed class Skill
{
    public Guid Id { get; init; }

    public Guid? ProjectId { get; init; }

    public string Code { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>Path to the Skill markdown, relative to the configured skills root.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public string? ContentHash { get; init; }

    public string? Description { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
