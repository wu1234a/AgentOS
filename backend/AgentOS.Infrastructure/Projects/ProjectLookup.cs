using AgentOS.Application.Projects;
using AgentOS.Domain.Projects;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Projects;

/// <summary>EF Core project lookup used by event validation.</summary>
public sealed class ProjectLookup(AgentOSDbContext dbContext) : IProjectLookup
{
    public Task<Project?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
        => dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(project => project.Slug == slug, cancellationToken);

    public Task<Project?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(project => project.Id == id, cancellationToken);
}
