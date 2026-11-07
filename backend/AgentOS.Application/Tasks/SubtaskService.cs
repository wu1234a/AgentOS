using System.Text.RegularExpressions;
using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Application.Tasks;

public interface ISubtaskRepository
{
    Task<AutomationTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken);

    Task<AutomationTask?> GetByPhaseAsync(Guid parentTaskId, string phaseKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<AutomationTask>> ListChildrenAsync(Guid parentTaskId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, TaskStatus>> GetDependencyStatusesAsync(
        IReadOnlyCollection<Guid> taskIds,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetDependencyIdsAsync(
        IReadOnlyCollection<Guid> taskIds,
        Guid projectId,
        CancellationToken cancellationToken);

    Task AddAsync(AutomationTask task, CancellationToken cancellationToken);

    Task AddDependenciesAsync(IReadOnlyList<TaskDependency> dependencies, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed class SubtaskService(
    ISubtaskRepository repository,
    TimeProvider timeProvider,
    ILogger<SubtaskService> logger)
{
    private const int MaxDependencies = 50;

    public async Task<SubtaskResult> CreateAsync(
        Guid parentTaskId,
        CreateSubtaskRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await repository.GetTaskAsync(parentTaskId, cancellationToken);
        if (parent is null)
        {
            return SubtaskResult.NotFound();
        }

        if (parent.ParentTaskId is not null)
        {
            return SubtaskResult.Invalid("nested-subtask", "parentTaskId", "Nested subtasks are not supported.");
        }

        if (parent.Status is TaskStatus.Succeeded or TaskStatus.Cancelled or TaskStatus.Failed)
        {
            return SubtaskResult.Conflict("parent-terminal");
        }

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is < 3 or > 200)
        {
            return SubtaskResult.Invalid("title-invalid", "title", "Title must be 3-200 characters.");
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is { Length: > 2000 })
        {
            return SubtaskResult.Invalid("description-too-long", "description", "Description must be 2000 characters or fewer.");
        }
        var phaseKey = string.IsNullOrWhiteSpace(request.PhaseKey) ? null : request.PhaseKey.Trim();
        if (phaseKey is not null && !Regex.IsMatch(phaseKey, "^[a-z0-9][a-z0-9._-]{0,63}$"))
        {
            return SubtaskResult.Invalid("phase-key-invalid", "phaseKey", "Invalid phase key.");
        }

        var dependencyIds = (request.DependsOnTaskIds ?? []).Distinct().ToArray();
        if (dependencyIds.Length > MaxDependencies)
        {
            return SubtaskResult.Invalid("too-many-dependencies", "dependsOnTaskIds", "At most 50 dependencies are allowed.");
        }

        if (dependencyIds.Any(id => id == parentTaskId))
        {
            return SubtaskResult.Invalid("self-dependency", "dependsOnTaskIds", "A task cannot depend on itself.");
        }

        var statuses = dependencyIds.Length == 0
            ? new Dictionary<Guid, TaskStatus>()
            : await repository.GetDependencyStatusesAsync(dependencyIds, parent.ProjectId, cancellationToken);

        if (dependencyIds.Any(id => !statuses.ContainsKey(id)))
        {
            return SubtaskResult.Invalid("dependency-not-found", "dependsOnTaskIds", "A dependency does not exist.");
        }

        var blocked = dependencyIds.Any(id => statuses[id] != TaskStatus.Succeeded);

        if (phaseKey is not null && await repository.GetByPhaseAsync(parentTaskId, phaseKey, cancellationToken) is not null)
        {
            return SubtaskResult.Conflict("phase-key-conflict");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var taskId = Guid.CreateVersion7();
        var task = new AutomationTask
        {
            Id = taskId,
            TaskCode = $"TASK-{taskId:N}".ToUpperInvariant(),
            ParentTaskId = parentTaskId,
            PhaseKey = phaseKey,
            ProjectId = parent.ProjectId,
            AgentId = parent.AgentId,
            SkillId = parent.SkillId,
            ModelProfileId = parent.ModelProfileId,
            LogicalTaskKey = $"subtask:{parentTaskId}:{taskId}",
            Status = blocked ? TaskStatus.Blocked : TaskStatus.Queued,
            Priority = parent.Priority,
            Title = title,
            Description = description,
            InputJson = "{}",
            MaxAttempts = parent.MaxAttempts,
            TimeoutSeconds = parent.TimeoutSeconds,
            NextRunAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await repository.AddAsync(task, cancellationToken);
        await repository.AddDependenciesAsync(
            dependencyIds.Select(id => new TaskDependency
            {
                TaskId = taskId,
                PrerequisiteTaskId = id,
                CreatedAtUtc = now
            }).ToList(),
            cancellationToken);
        await repository.SaveAsync(cancellationToken);

        logger.LogInformation("Created subtask {TaskId} under {ParentTaskId}.", taskId, parentTaskId);
        var dependencies = await repository.GetDependencyIdsAsync([taskId], parent.ProjectId, cancellationToken);
        return SubtaskResult.Ok(ToView(task, dependencies.GetValueOrDefault(taskId) ?? [], statuses));
    }

    public async Task<SubtaskListResult?> ListAsync(Guid parentTaskId, CancellationToken cancellationToken)
    {
        var parent = await repository.GetTaskAsync(parentTaskId, cancellationToken);
        if (parent is null)
        {
            return null;
        }

        var children = await repository.ListChildrenAsync(parentTaskId, cancellationToken);
        var dependencyMap = await repository.GetDependencyIdsAsync(children.Select(child => child.Id).ToArray(), parent.ProjectId, cancellationToken);
        var allDependencies = dependencyMap.Values.SelectMany(ids => ids).Distinct().ToArray();
        var states = await repository.GetDependencyStatusesAsync(allDependencies, parent.ProjectId, cancellationToken);
        var views = children
            .Select(child => ToView(child, dependencyMap.GetValueOrDefault(child.Id) ?? [], states))
            .ToList();

        var summary = new SubtaskSummary(
            children.Count,
            children.Count(task => task.Status == TaskStatus.Queued),
            children.Count(task => task.Status == TaskStatus.Running),
            children.Count(task => task.Status == TaskStatus.Blocked),
            children.Count(task => task.Status == TaskStatus.Succeeded),
            children.Count(task => task.Status == TaskStatus.Failed),
            children.Count(task => task.Status is not (TaskStatus.Queued or TaskStatus.Running or TaskStatus.Blocked or TaskStatus.Succeeded or TaskStatus.Failed)),
            children.Count > 0 && children.All(task => task.Status == TaskStatus.Succeeded));

        return new SubtaskListResult(parentTaskId, parent.Status.ToString(), views, summary);
    }

    private static SubtaskView ToView(
        AutomationTask task,
        IReadOnlyList<Guid> dependencies,
        IReadOnlyDictionary<Guid, TaskStatus> statuses)
        => new(
            task.Id,
            task.ParentTaskId ?? Guid.Empty,
            task.ProjectId,
            task.Title,
            task.Description,
            task.PhaseKey,
            task.Status.ToString(),
            dependencies,
            dependencies.Where(id => !statuses.TryGetValue(id, out var status) || status != TaskStatus.Succeeded).ToList(),
            task.CreatedAtUtc,
            task.UpdatedAtUtc);
}
