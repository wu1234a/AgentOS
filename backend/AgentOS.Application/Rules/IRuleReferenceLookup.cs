namespace AgentOS.Application.Rules;

/// <summary>Reference data a rule can point at; rules may only use enabled targets.</summary>
public sealed record RuleProjectReference(Guid Id, string Slug, bool IsEnabled);

public sealed record RuleAgentReference(Guid Id, string Code, Guid? ProjectId, bool IsEnabled);

public sealed record RuleSkillReference(Guid Id, string Code, string Version, Guid? ProjectId, bool IsEnabled);

public sealed record RuleModelProfileReference(Guid Id, string Code, bool IsEnabled);

/// <summary>Read-only lookups used while validating and normalizing rules.</summary>
public interface IRuleReferenceLookup
{
    Task<RuleProjectReference?> FindProjectAsync(string slug, CancellationToken cancellationToken);

    Task<RuleAgentReference?> FindAgentAsync(string code, Guid? projectId, CancellationToken cancellationToken);

    Task<RuleSkillReference?> FindSkillAsync(string code, Guid? projectId, CancellationToken cancellationToken);

    Task<RuleModelProfileReference?> FindModelProfileAsync(string code, CancellationToken cancellationToken);
}
