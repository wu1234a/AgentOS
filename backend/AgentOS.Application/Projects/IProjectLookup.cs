using AgentOS.Domain.Projects;

namespace AgentOS.Application.Projects;

/// <summary>Read-only project lookup used when validating event project scope.</summary>
public interface IProjectLookup
{
    Task<Project?> FindBySlugAsync(string slug, CancellationToken cancellationToken);

    Task<Project?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
