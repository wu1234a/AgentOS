namespace AgentOS.Domain.Tasks;

/// <summary>Finish-to-start dependency between two tasks (AOS-016).</summary>
public sealed class TaskDependency
{
    public Guid TaskId { get; init; }

    public Guid PrerequisiteTaskId { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
