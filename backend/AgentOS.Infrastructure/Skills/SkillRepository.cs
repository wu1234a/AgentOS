using AgentOS.Application.Skills;
using AgentOS.Domain.Skills;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Skills;

/// <summary>EF Core implementation of the skill version registry.</summary>
public sealed class SkillRepository(AgentOSDbContext dbContext) : ISkillRepository
{
    public async Task<(IReadOnlyList<SkillView> Items, int Total)> ListAsync(
        Guid? projectId,
        string? code,
        bool includeDisabled,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Skills.AsNoTracking();

        if (!includeDisabled)
        {
            query = query.Where(skill => skill.IsEnabled);
        }

        if (projectId is not null)
        {
            query = query.Where(skill => skill.ProjectId == projectId);
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var normalizedCode = code.Trim().ToLowerInvariant();
            query = query.Where(skill => skill.Code == normalizedCode);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(skill => skill.CreatedAtUtc)
            .ThenByDescending(skill => skill.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var projectIds = rows
            .Where(skill => skill.ProjectId is not null)
            .Select(skill => skill.ProjectId!.Value)
            .Distinct()
            .ToArray();

        var projectSlugs = await dbContext.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .ToDictionaryAsync(project => project.Id, project => project.Slug, cancellationToken);

        var items = rows
            .Select(skill => ToView(
                skill,
                skill.ProjectId is { } id && projectSlugs.TryGetValue(id, out var slug) ? slug : null))
            .ToList();

        return (items, total);
    }

    public Task<Skill?> GetTrackedAsync(Guid skillId, CancellationToken cancellationToken)
        => dbContext.Skills.FirstOrDefaultAsync(skill => skill.Id == skillId, cancellationToken);

    public async Task<SkillView?> GetViewAsync(Guid skillId, CancellationToken cancellationToken)
    {
        var skill = await dbContext.Skills
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == skillId, cancellationToken);

        if (skill is null)
        {
            return null;
        }

        var slug = skill.ProjectId is { } projectId
            ? await dbContext.Projects
                .AsNoTracking()
                .Where(project => project.Id == projectId)
                .Select(project => project.Slug)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return ToView(skill, slug);
    }

    public Task<bool> ProjectEnabledAsync(Guid projectId, CancellationToken cancellationToken)
        => dbContext.Projects
            .AsNoTracking()
            .AnyAsync(project => project.Id == projectId && project.IsEnabled, cancellationToken);

    public Task<bool> VersionExistsAsync(
        Guid? projectId,
        string code,
        string version,
        CancellationToken cancellationToken)
        => dbContext.Skills.AnyAsync(
            skill => skill.ProjectId == projectId && skill.Code == code && skill.Version == version,
            cancellationToken);

    public async Task<bool> TryAddAsync(Skill skill, CancellationToken cancellationToken)
    {
        dbContext.Skills.Add(skill);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (SqlErrorHelper.IsUniqueKeyViolation(exception))
        {
            dbContext.Entry(skill).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private static SkillView ToView(Skill skill, string? projectSlug)
        => new(
            skill.Id,
            skill.ProjectId,
            projectSlug,
            skill.Code,
            skill.Version,
            skill.RelativePath,
            skill.ContentHash,
            skill.Description,
            skill.IsEnabled,
            skill.CreatedAtUtc,
            skill.UpdatedAtUtc);
}
