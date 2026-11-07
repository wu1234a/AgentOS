using AgentOS.Application.Projects;
using AgentOS.Domain.Projects;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Projects;

public sealed class ProjectRepository(AgentOSDbContext dbContext) : IProjectRepository
{
    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken)
        => await dbContext.Projects.AsNoTracking().OrderBy(project => project.Slug).ToListAsync(cancellationToken);

    public Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Projects.FirstOrDefaultAsync(project => project.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken)
        => dbContext.Projects.AnyAsync(project => project.Slug == slug, cancellationToken);

    public async Task<bool> TryAddAsync(Project project, CancellationToken cancellationToken)
    {
        dbContext.Projects.Add(project);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(project).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
