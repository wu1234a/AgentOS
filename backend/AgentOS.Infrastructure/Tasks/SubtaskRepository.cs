using AgentOS.Application.Tasks;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Infrastructure.Tasks;

public sealed class SubtaskRepository(AgentOSDbContext dbContext) : ISubtaskRepository
{
    public Task<AutomationTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken)
        => dbContext.Tasks.FirstOrDefaultAsync(task => task.Id == taskId, cancellationToken);

    public Task<AutomationTask?> GetByPhaseAsync(
        Guid parentTaskId,
        string phaseKey,
        CancellationToken cancellationToken)
        => dbContext.Tasks.FirstOrDefaultAsync(
            task => task.ParentTaskId == parentTaskId && task.PhaseKey == phaseKey,
            cancellationToken);

    public async Task<IReadOnlyList<AutomationTask>> ListChildrenAsync(
        Guid parentTaskId,
        CancellationToken cancellationToken)
        => await dbContext.Tasks
            .AsNoTracking()
            .Where(task => task.ParentTaskId == parentTaskId)
            .OrderBy(task => task.CreatedAtUtc)
            .ThenBy(task => task.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, TaskStatus>> GetDependencyStatusesAsync(
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
        {
            return new Dictionary<Guid, TaskStatus>();
        }

        return await dbContext.Tasks
            .AsNoTracking()
            .Where(task => taskIds.Contains(task.Id))
            .ToDictionaryAsync(task => task.Id, task => task.Status, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetDependencyIdsAsync(
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        }

        var rows = await dbContext.TaskDependencies
            .AsNoTracking()
            .Where(dependency => taskIds.Contains(dependency.TaskId))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(dependency => dependency.TaskId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)group.Select(dependency => dependency.PrerequisiteTaskId).ToList());
    }

    public async Task AddAsync(AutomationTask task, CancellationToken cancellationToken)
        => await dbContext.Tasks.AddAsync(task, cancellationToken);

    public async Task AddDependenciesAsync(
        IReadOnlyList<TaskDependency> dependencies,
        CancellationToken cancellationToken)
        => await dbContext.TaskDependencies.AddRangeAsync(dependencies, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
