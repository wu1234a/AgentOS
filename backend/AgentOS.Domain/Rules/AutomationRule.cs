namespace AgentOS.Domain.Rules;

/// <summary>
/// A routing rule. Rules decide what work should exist; they never execute an agent.
/// <see cref="SourcePattern"/> and <see cref="EventTypePattern"/> are glob patterns
/// (<c>*</c> matches any run of characters); an empty value or <c>*</c> matches everything.
/// </summary>
public sealed class AutomationRule
{
    public Guid Id { get; init; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Null means the rule applies to every project.</summary>
    public Guid? ProjectId { get; set; }

    public string SourcePattern { get; set; } = "*";

    public string EventTypePattern { get; set; } = "*";

    /// <summary>Optional structured conditions; see <c>RuleMatchConditions</c>.</summary>
    public string MatchJson { get; set; } = "{}";

    public Guid AgentId { get; set; }

    public Guid? SkillId { get; set; }

    public Guid? ModelProfileId { get; set; }

    /// <summary>Dispatch parameters; see <c>RuleDispatchSettings</c>.</summary>
    public string DispatchJson { get; set; } = "{}";

    /// <summary>Higher values are evaluated first; ties are broken by a stable order.</summary>
    public int Priority { get; set; }

    public int MaxParallel { get; set; } = 1;

    public int MaxAttempts { get; set; } = 2;

    public int TimeoutSeconds { get; set; } = 1800;

    public string NotificationPolicy { get; set; } = "none";

    public bool IsEnabled { get; set; }

    /// <summary>Incremented on every update so tasks can record the exact rule revision.</summary>
    public int Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
