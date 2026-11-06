using AgentOS.Application.Agents;
using AgentOS.Domain.Agents;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Infrastructure.Agents;

/// <summary>EF Core implementation of the agent registry.</summary>
public sealed class AgentRepository(AgentOSDbContext dbContext) : IAgentRepository
{
    public async Task<(IReadOnlyList<AgentSummary> Items, int Total)> ListAsync(
        Guid? projectId,
        string? role,
        bool includeArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Agents.AsNoTracking();

        if (!includeArchived)
        {
            query = query.Where(agent => agent.ArchivedAtUtc == null);
        }

        if (projectId is not null)
        {
            query = query.Where(agent => agent.ProjectId == projectId);
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalizedRole = role.Trim().ToLowerInvariant();
            query = query.Where(agent => agent.Role == normalizedRole);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(agent => agent.Code)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var projectIds = rows
            .Where(agent => agent.ProjectId is not null)
            .Select(agent => agent.ProjectId!.Value)
            .Distinct()
            .ToArray();

        var projectSlugs = await dbContext.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .ToDictionaryAsync(project => project.Id, project => project.Slug, cancellationToken);

        var items = rows
            .Select(agent => ToSummary(
                agent,
                agent.ProjectId is { } id && projectSlugs.TryGetValue(id, out var slug) ? slug : null))
            .ToList();

        return (items, total);
    }

    public async Task<AgentSummary?> GetSummaryAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == agentId, cancellationToken);

        if (agent is null)
        {
            return null;
        }

        var slug = agent.ProjectId is { } projectId
            ? await dbContext.Projects
                .AsNoTracking()
                .Where(project => project.Id == projectId)
                .Select(project => project.Slug)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return ToSummary(agent, slug);
    }

    public Task<Agent?> GetTrackedAsync(Guid agentId, CancellationToken cancellationToken)
        => dbContext.Agents.FirstOrDefaultAsync(agent => agent.Id == agentId, cancellationToken);

    public Task<bool> CodeExistsAsync(
        Guid? projectId,
        string code,
        Guid? excludeAgentId,
        CancellationToken cancellationToken)
        => dbContext.Agents
            .AsNoTracking()
            .AnyAsync(
                agent => agent.Code == code
                         && agent.ProjectId == projectId
                         && (excludeAgentId == null || agent.Id != excludeAgentId),
                cancellationToken);

    public Task<bool> ProjectEnabledAsync(Guid projectId, CancellationToken cancellationToken)
        => dbContext.Projects
            .AsNoTracking()
            .AnyAsync(project => project.Id == projectId && project.IsEnabled, cancellationToken);

    public Task<bool> ModelProfileEnabledAsync(Guid modelProfileId, CancellationToken cancellationToken)
        => dbContext.ModelProfiles
            .AsNoTracking()
            .AnyAsync(profile => profile.Id == modelProfileId && profile.IsEnabled, cancellationToken);

    public Task<int> CountActiveTasksAsync(Guid agentId, CancellationToken cancellationToken)
        => dbContext.Tasks.CountAsync(
            task => task.AgentId == agentId
                    && (task.Status == TaskStatus.Queued
                        || task.Status == TaskStatus.Running
                        || task.Status == TaskStatus.RetryWait),
            cancellationToken);

    public async Task<bool> TryAddAsync(Agent agent, CancellationToken cancellationToken)
    {
        dbContext.Agents.Add(agent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (SqlErrorHelper.IsUniqueKeyViolation(exception))
        {
            dbContext.Entry(agent).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private static AgentSummary ToSummary(Agent agent, string? projectSlug)
        => new(
            agent.Id,
            agent.ProjectId,
            projectSlug,
            agent.Code,
            agent.DisplayName,
            agent.Role,
            agent.Description,
            agent.PermissionProfile,
            agent.DefaultModelProfileId,
            agent.SystemPromptPath,
            agent.IsEnabled,
            agent.ArchivedAtUtc,
            agent.CreatedAtUtc,
            agent.UpdatedAtUtc);
}
