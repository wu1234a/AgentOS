using AgentOS.Application.Rules;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Rules;

/// <summary>EF Core implementation of the reference lookups used by rule validation.</summary>
public sealed class RuleReferenceLookup(AgentOSDbContext dbContext) : IRuleReferenceLookup
{
    public async Task<RuleProjectReference?> FindProjectAsync(string slug, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Slug == slug, cancellationToken);

        return project is null ? null : new RuleProjectReference(project.Id, project.Slug, project.IsEnabled);
    }

    public async Task<RuleAgentReference?> FindAgentAsync(
        string code,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .AsNoTracking()
            .Where(candidate => candidate.Code == code
                                && (candidate.ProjectId == null || candidate.ProjectId == projectId))
            .OrderByDescending(candidate => candidate.ProjectId != null)
            .FirstOrDefaultAsync(cancellationToken);

        return agent is null ? null : new RuleAgentReference(agent.Id, agent.Code, agent.ProjectId, agent.IsEnabled);
    }

    public async Task<RuleSkillReference?> FindSkillAsync(
        string code,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        // A rule references a skill by code; the newest visible version wins.
        var skill = await dbContext.Skills
            .AsNoTracking()
            .Where(candidate => candidate.Code == code
                                && (candidate.ProjectId == null || candidate.ProjectId == projectId))
            .OrderByDescending(candidate => candidate.ProjectId != null)
            .ThenByDescending(candidate => candidate.CreatedAtUtc)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return skill is null
            ? null
            : new RuleSkillReference(skill.Id, skill.Code, skill.Version, skill.ProjectId, skill.IsEnabled);
    }

    public async Task<RuleModelProfileReference?> FindModelProfileAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.ModelProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);

        return profile is null ? null : new RuleModelProfileReference(profile.Id, profile.Code, profile.IsEnabled);
    }
}
