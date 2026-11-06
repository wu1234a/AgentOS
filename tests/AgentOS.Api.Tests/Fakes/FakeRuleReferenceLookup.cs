using AgentOS.Application.Rules;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>In-memory reference data for rule validation tests.</summary>
public sealed class FakeRuleReferenceLookup : IRuleReferenceLookup
{
    public Dictionary<string, RuleProjectReference> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, RuleAgentReference> Agents { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, RuleSkillReference> Skills { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, RuleModelProfileReference> ModelProfiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static FakeRuleReferenceLookup Default()
    {
        var lookup = new FakeRuleReferenceLookup();
        var projectId = Guid.Parse("0199c0de-0000-7000-8000-000000000001");

        lookup.Projects["3derp"] = new RuleProjectReference(projectId, "3derp", true);
        lookup.Projects["disabled-demo"] = new RuleProjectReference(Guid.CreateVersion7(), "disabled-demo", false);

        lookup.Agents["planner"] = new RuleAgentReference(Guid.CreateVersion7(), "planner", null, true);
        lookup.Agents["disabled-agent"] = new RuleAgentReference(Guid.CreateVersion7(), "disabled-agent", null, false);

        lookup.Skills["3derp-plan-development"] =
            new RuleSkillReference(Guid.CreateVersion7(), "3derp-plan-development", "1.0", projectId, true);
        lookup.Skills["disabled-skill"] =
            new RuleSkillReference(Guid.CreateVersion7(), "disabled-skill", "1.0", null, false);

        lookup.ModelProfiles["strong-reasoning"] =
            new RuleModelProfileReference(Guid.CreateVersion7(), "strong-reasoning", true);
        lookup.ModelProfiles["disabled-profile"] =
            new RuleModelProfileReference(Guid.CreateVersion7(), "disabled-profile", false);

        return lookup;
    }

    public Task<RuleProjectReference?> FindProjectAsync(string slug, CancellationToken cancellationToken)
        => Task.FromResult(Projects.GetValueOrDefault(slug));

    public Task<RuleAgentReference?> FindAgentAsync(string code, Guid? projectId, CancellationToken cancellationToken)
        => Task.FromResult(Agents.GetValueOrDefault(code));

    public Task<RuleSkillReference?> FindSkillAsync(string code, Guid? projectId, CancellationToken cancellationToken)
        => Task.FromResult(Skills.GetValueOrDefault(code));

    public Task<RuleModelProfileReference?> FindModelProfileAsync(string code, CancellationToken cancellationToken)
        => Task.FromResult(ModelProfiles.GetValueOrDefault(code));
}
