namespace AgentOS.Domain.Agents;

/// <summary>An Agent role (planner, coder, reviewer, qa) that rules can dispatch work to.</summary>
public sealed class Agent
{
    public Guid Id { get; init; }

    /// <summary>Null for a global agent; otherwise the project that owns it.</summary>
    public Guid? ProjectId { get; init; }

    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid? DefaultModelProfileId { get; set; }

    public string? SystemPromptPath { get; set; }

    public string PermissionProfile { get; set; } = "read-only";

    public bool IsEnabled { get; set; }

    /// <summary>Set when the agent is archived; archived agents are read-only and hidden by default.</summary>
    public DateTime? ArchivedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
