using AgentOS.Application.ModelProfiles;
using AgentOS.Domain.ModelProfiles;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.ModelProfiles;

/// <summary>EF Core implementation of the model profile registry and resolution.</summary>
public sealed class ModelProfileRepository(AgentOSDbContext dbContext) : IModelProfileRepository
{
    public async Task<(IReadOnlyList<ModelProfile> Items, int Total)> ListAsync(
        bool? enabled,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ModelProfiles.AsNoTracking();

        if (enabled is not null)
        {
            query = query.Where(profile => profile.IsEnabled == enabled);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(profile => profile.Code)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<ModelProfile?> GetAsync(Guid profileId, CancellationToken cancellationToken)
        => dbContext.ModelProfiles.FirstOrDefaultAsync(profile => profile.Id == profileId, cancellationToken);

    public Task<ModelProfile?> FindByCodeAsync(string code, CancellationToken cancellationToken)
        => dbContext.ModelProfiles.FirstOrDefaultAsync(profile => profile.Code == code, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code,
        Guid? excludeProfileId,
        CancellationToken cancellationToken)
        => dbContext.ModelProfiles.AnyAsync(
            profile => profile.Code == code && (excludeProfileId == null || profile.Id != excludeProfileId),
            cancellationToken);

    public async Task<bool> TryAddAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        dbContext.ModelProfiles.Add(profile);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (SqlErrorHelper.IsUniqueKeyViolation(exception))
        {
            dbContext.Entry(profile).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<ResolvedModelProfile?> ResolveAsync(
        Guid? taskId,
        Guid? ruleId,
        Guid? agentId,
        CancellationToken cancellationToken)
    {
        Guid? taskProfileId = null;
        Guid? resolvedRuleId = ruleId;
        Guid? resolvedAgentId = agentId;

        if (taskId is { } id)
        {
            var task = await dbContext.Tasks
                .AsNoTracking()
                .Where(candidate => candidate.Id == id)
                .Select(candidate => new
                {
                    candidate.ModelProfileId,
                    candidate.RuleId,
                    candidate.AgentId
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (task is null)
            {
                return null;
            }

            taskProfileId = task.ModelProfileId;
            resolvedRuleId ??= task.RuleId;
            resolvedAgentId ??= task.AgentId;
        }

        if (taskProfileId is { } taskProfile
            && await FindEnabledAsync(taskProfile, cancellationToken) is { } fromTask)
        {
            return ToResolved(fromTask, "task");
        }

        if (resolvedRuleId is { } ruleKey)
        {
            var rule = await dbContext.AutomationRules
                .AsNoTracking()
                .Where(candidate => candidate.Id == ruleKey)
                .Select(candidate => new { candidate.ModelProfileId, candidate.AgentId })
                .FirstOrDefaultAsync(cancellationToken);

            if (rule is not null)
            {
                resolvedAgentId ??= rule.AgentId;

                if (rule.ModelProfileId is { } ruleProfile
                    && await FindEnabledAsync(ruleProfile, cancellationToken) is { } fromRule)
                {
                    return ToResolved(fromRule, "rule");
                }
            }
        }

        if (resolvedAgentId is { } agentKey)
        {
            var agentProfileId = await dbContext.Agents
                .AsNoTracking()
                .Where(agent => agent.Id == agentKey)
                .Select(agent => agent.DefaultModelProfileId)
                .FirstOrDefaultAsync(cancellationToken);

            if (agentProfileId is { } agentProfile
                && await FindEnabledAsync(agentProfile, cancellationToken) is { } fromAgent)
            {
                return ToResolved(fromAgent, "agent");
            }
        }

        return null;
    }

    private Task<ModelProfile?> FindEnabledAsync(Guid profileId, CancellationToken cancellationToken)
        => dbContext.ModelProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.Id == profileId && profile.IsEnabled, cancellationToken);

    private static ResolvedModelProfile ToResolved(ModelProfile profile, string source)
        => new(
            profile.Id,
            profile.Code,
            profile.Provider,
            profile.Model,
            profile.ExecutionMode,
            profile.ConfigurationJson,
            profile.SecretReference,
            profile.TimeoutSeconds,
            source);
}
