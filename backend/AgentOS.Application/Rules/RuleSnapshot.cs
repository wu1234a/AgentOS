namespace AgentOS.Application.Rules;

/// <summary>
/// Read model of an enabled rule together with the reference checks the dispatcher needs.
/// Built by the persistence layer so matching stays a pure function.
/// </summary>
public sealed record RuleSnapshot(
    Guid Id,
    string Name,
    Guid? ProjectId,
    string SourcePattern,
    string EventTypePattern,
    RuleMatchConditions Conditions,
    int Priority,
    Guid AgentId,
    Guid? SkillId,
    Guid? ModelProfileId,
    string DispatchKey,
    string TaskPriority,
    int MaxAttempts,
    int TimeoutSeconds,
    int Version,
    DateTime CreatedAtUtc);
