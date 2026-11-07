using AgentOS.Domain.Tasks;

namespace AgentOS.Application.Tasks;

/// <summary>Releases blocked tasks whose prerequisites succeeded and fails those whose prerequisites failed.</summary>
public interface ITaskDependencyCoordinator
{
    Task ReleaseAndPropagateAsync(DateTime nowUtc, CancellationToken cancellationToken);
}
