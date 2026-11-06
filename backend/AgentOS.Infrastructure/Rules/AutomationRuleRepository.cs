using AgentOS.Application.Rules;
using AgentOS.Domain.Rules;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Rules;

/// <summary>EF Core implementation of rule management queries.</summary>
public sealed class AutomationRuleRepository(AgentOSDbContext dbContext) : IAutomationRuleRepository
{
    public async Task<RulePage> ListAsync(RuleListQuery query, CancellationToken cancellationToken)
    {
        var rules = dbContext.AutomationRules.AsNoTracking().AsQueryable();

        if (query.IsEnabled is not null)
        {
            rules = rules.Where(rule => rule.IsEnabled == query.IsEnabled);
        }

        if (!string.IsNullOrWhiteSpace(query.ProjectSlug))
        {
            rules = rules.Where(rule =>
                rule.ProjectId != null
                && dbContext.Projects.Any(project => project.Id == rule.ProjectId && project.Slug == query.ProjectSlug));
        }

        var total = await rules.CountAsync(cancellationToken);
        var skip = (long)(query.Page - 1) * query.PageSize;
        if (skip > int.MaxValue)
        {
            return new RulePage([], query.Page, query.PageSize, total);
        }

        var items = await (
                from rule in rules
                orderby rule.IsEnabled descending, rule.Priority descending, rule.UpdatedAtUtc descending, rule.Id
                select new
                {
                    Rule = rule,
                    ProjectSlug = dbContext.Projects
                        .Where(project => project.Id == rule.ProjectId)
                        .Select(project => project.Slug)
                        .FirstOrDefault(),
                    AgentCode = dbContext.Agents
                        .Where(agent => agent.Id == rule.AgentId)
                        .Select(agent => agent.Code)
                        .FirstOrDefault(),
                    SkillCode = rule.SkillId == null
                        ? null
                        : dbContext.Skills
                            .Where(skill => skill.Id == rule.SkillId)
                            .Select(skill => skill.Code)
                            .FirstOrDefault(),
                    ModelProfileCode = rule.ModelProfileId == null
                        ? null
                        : dbContext.ModelProfiles
                            .Where(profile => profile.Id == rule.ModelProfileId)
                            .Select(profile => profile.Code)
                            .FirstOrDefault()
                })
            .Skip((int)skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var summaries = items
            .Select(item => new RuleSummary(
                item.Rule.Id,
                item.Rule.Name,
                item.ProjectSlug,
                item.Rule.SourcePattern,
                item.Rule.EventTypePattern,
                item.AgentCode ?? string.Empty,
                item.SkillCode,
                item.ModelProfileCode,
                item.Rule.Priority,
                item.Rule.MaxParallel,
                item.Rule.MaxAttempts,
                item.Rule.TimeoutSeconds,
                item.Rule.NotificationPolicy,
                item.Rule.IsEnabled,
                item.Rule.Version,
                item.Rule.UpdatedAtUtc))
            .ToArray();

        return new RulePage(summaries, query.Page, query.PageSize, total);
    }

    public Task<RuleDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
        => (
                from rule in dbContext.AutomationRules.AsNoTracking()
                where rule.Id == id
                select new
                {
                    Rule = rule,
                    ProjectSlug = dbContext.Projects
                        .Where(project => project.Id == rule.ProjectId)
                        .Select(project => project.Slug)
                        .FirstOrDefault(),
                    AgentCode = dbContext.Agents
                        .Where(agent => agent.Id == rule.AgentId)
                        .Select(agent => agent.Code)
                        .FirstOrDefault(),
                    SkillCode = rule.SkillId == null
                        ? null
                        : dbContext.Skills
                            .Where(skill => skill.Id == rule.SkillId)
                            .Select(skill => skill.Code)
                            .FirstOrDefault(),
                    ModelProfileCode = rule.ModelProfileId == null
                        ? null
                        : dbContext.ModelProfiles
                            .Where(profile => profile.Id == rule.ModelProfileId)
                            .Select(profile => profile.Code)
                            .FirstOrDefault()
                })
            .Select(item => new RuleDetail(
                item.Rule.Id,
                item.Rule.Name,
                item.Rule.ProjectId,
                item.ProjectSlug,
                item.Rule.SourcePattern,
                item.Rule.EventTypePattern,
                item.AgentCode ?? string.Empty,
                item.Rule.AgentId,
                item.SkillCode,
                item.Rule.SkillId,
                item.ModelProfileCode,
                item.Rule.ModelProfileId,
                System.Text.Json.JsonDocument.Parse(item.Rule.MatchJson).RootElement.Clone(),
                System.Text.Json.JsonDocument.Parse(item.Rule.DispatchJson).RootElement.Clone(),
                item.Rule.Priority,
                item.Rule.MaxParallel,
                item.Rule.MaxAttempts,
                item.Rule.TimeoutSeconds,
                item.Rule.NotificationPolicy,
                item.Rule.IsEnabled,
                item.Rule.Version,
                item.Rule.CreatedAtUtc,
                item.Rule.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<AutomationRule?> FindEntityAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.AutomationRules.FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

    public async Task AddAsync(AutomationRule rule, CancellationToken cancellationToken)
    {
        dbContext.AutomationRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
