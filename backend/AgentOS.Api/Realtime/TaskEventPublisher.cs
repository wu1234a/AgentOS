using AgentOS.Application.Tasks;
using AgentOS.Api.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace AgentOS.Api.Realtime;

/// <summary>Publishes lightweight UI notifications; failures never affect the API response.</summary>
public static class TaskEventPublisher
{
    public static async Task PublishAsync(
        IHubContext<TaskHub> hub,
        TaskQueueResult result,
        CancellationToken cancellationToken)
    {
        if (result.Task is null)
        {
            return;
        }

        try
        {
            await hub.Clients.All.SendAsync(
                "taskChanged",
                new
                {
                    taskId = result.Task.Id,
                    status = result.Task.Status.ToString(),
                    projectId = result.Task.ProjectId
                },
                cancellationToken);
        }
        catch (Exception)
        {
            // SignalR is best effort; the database and REST API remain authoritative.
        }
    }

    public static async Task PublishClaimAsync(
        IHubContext<TaskHub> hub,
        IReadOnlyList<ClaimedTask> claimed,
        CancellationToken cancellationToken)
    {
        if (claimed.Count == 0)
        {
            return;
        }

        try
        {
            await hub.Clients.All.SendAsync(
                "tasksClaimed",
                claimed.Select(task => new { taskId = task.TaskId, projectSlug = task.ProjectSlug }),
                cancellationToken);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
