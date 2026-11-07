using AgentOS.Application.Tasks;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Infrastructure.Tasks;

public sealed class TaskDependencyCoordinator(AgentOSDbContext dbContext) : ITaskDependencyCoordinator
{
    public async Task ReleaseAndPropagateAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        for (var pass = 0; pass < 20; pass++)
        {
            var blocked = await dbContext.Tasks
                .Where(task => task.Status == TaskStatus.Blocked)
                .OrderBy(task => task.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            if (blocked.Count == 0)
            {
                return;
            }

            var ids = blocked.Select(task => task.Id).ToArray();
            var dependencies = await dbContext.TaskDependencies
                .AsNoTracking()
                .Where(dependency => ids.Contains(dependency.TaskId))
                .ToListAsync(cancellationToken);
            var prerequisiteIds = dependencies.Select(dependency => dependency.PrerequisiteTaskId).Distinct().ToArray();
            var prerequisiteStatuses = await dbContext.Tasks
                .AsNoTracking()
                .Where(task => prerequisiteIds.Contains(task.Id))
                .ToDictionaryAsync(task => task.Id, task => task.Status, cancellationToken);

            var changed = false;
            foreach (var task in blocked)
            {
                var prerequisites = dependencies
                    .Where(dependency => dependency.TaskId == task.Id)
                    .Select(dependency => prerequisiteStatuses.GetValueOrDefault(dependency.PrerequisiteTaskId))
                    .ToList();

                if (prerequisites.Any(status => status is TaskStatus.Failed or TaskStatus.Cancelled))
                {
                    task.Status = TaskStatus.Failed;
                    task.ErrorCode = "dependency-failed";
                    task.ErrorMessage = "A prerequisite task failed or was cancelled.";
                    task.CompletedAtUtc = nowUtc;
                    task.UpdatedAtUtc = nowUtc;
                    changed = true;
                }
                else if (prerequisites.Count > 0 && prerequisites.All(status => status == TaskStatus.Succeeded))
                {
                    task.Status = TaskStatus.Queued;
                    task.NextRunAtUtc = nowUtc;
                    task.UpdatedAtUtc = nowUtc;
                    changed = true;
                }
            }

            if (changed)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                return;
            }
        }
    }
}
