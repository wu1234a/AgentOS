using System.Text.Json;
using AgentOS.Application.Rules;
using AgentOS.Domain.Rules;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>In-memory rule store used by API tests.</summary>
public sealed class FakeAutomationRuleRepository(FakeRuleReferenceLookup references) : IAutomationRuleRepository
{
    private readonly List<AutomationRule> _rules = [];

    public IReadOnlyList<AutomationRule> Entities => _rules;

    public Task<RulePage> ListAsync(RuleListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<AutomationRule> rules = _rules;

        if (query.IsEnabled is not null)
        {
            rules = rules.Where(rule => rule.IsEnabled == query.IsEnabled);
        }

        if (!string.IsNullOrWhiteSpace(query.ProjectSlug))
        {
            var projectId = references.Projects.GetValueOrDefault(query.ProjectSlug)?.Id;
            rules = rules.Where(rule => rule.ProjectId == projectId);
        }

        var ordered = rules
            .OrderByDescending(rule => rule.IsEnabled)
            .ThenByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.UpdatedAtUtc)
            .ThenBy(rule => rule.Id)
            .ToArray();

        var total = ordered.Length;
        var items = ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(BuildSummary)
            .ToArray();

        return Task.FromResult(new RulePage(items, query.Page, query.PageSize, total));
    }

    public Task<RuleDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var rule = _rules.FirstOrDefault(candidate => candidate.Id == id);
        return Task.FromResult(rule is null ? null : BuildDetail(rule));
    }

    public Task<AutomationRule?> FindEntityAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_rules.FirstOrDefault(candidate => candidate.Id == id));

    public Task AddAsync(AutomationRule rule, CancellationToken cancellationToken)
    {
        _rules.Add(rule);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private string? ProjectSlug(Guid? projectId)
        => projectId is null
            ? null
            : references.Projects.Values.FirstOrDefault(project => project.Id == projectId)?.Slug;

    private string AgentCode(Guid agentId)
        => references.Agents.Values.FirstOrDefault(agent => agent.Id == agentId)?.Code ?? string.Empty;

    private string? SkillCode(Guid? skillId)
        => skillId is null
            ? null
            : references.Skills.Values.FirstOrDefault(skill => skill.Id == skillId)?.Code;

    private string? ModelProfileCode(Guid? profileId)
        => profileId is null
            ? null
            : references.ModelProfiles.Values.FirstOrDefault(profile => profile.Id == profileId)?.Code;

    private RuleSummary BuildSummary(AutomationRule rule) => new(
        rule.Id,
        rule.Name,
        ProjectSlug(rule.ProjectId),
        rule.SourcePattern,
        rule.EventTypePattern,
        AgentCode(rule.AgentId),
        SkillCode(rule.SkillId),
        ModelProfileCode(rule.ModelProfileId),
        rule.Priority,
        rule.MaxParallel,
        rule.MaxAttempts,
        rule.TimeoutSeconds,
        rule.NotificationPolicy,
        rule.IsEnabled,
        rule.Version,
        rule.UpdatedAtUtc);

    private RuleDetail BuildDetail(AutomationRule rule) => new(
        rule.Id,
        rule.Name,
        rule.ProjectId,
        ProjectSlug(rule.ProjectId),
        rule.SourcePattern,
        rule.EventTypePattern,
        AgentCode(rule.AgentId),
        rule.AgentId,
        SkillCode(rule.SkillId),
        rule.SkillId,
        ModelProfileCode(rule.ModelProfileId),
        rule.ModelProfileId,
        JsonDocument.Parse(rule.MatchJson).RootElement.Clone(),
        JsonDocument.Parse(rule.DispatchJson).RootElement.Clone(),
        rule.Priority,
        rule.MaxParallel,
        rule.MaxAttempts,
        rule.TimeoutSeconds,
        rule.NotificationPolicy,
        rule.IsEnabled,
        rule.Version,
        rule.CreatedAtUtc,
        rule.UpdatedAtUtc);
}
