using AgentOS.Api.Filters;
using AgentOS.Application.Tasks;

namespace AgentOS.Api.Endpoints;

/// <summary>Administrative task endpoints introduced by AOS-005.</summary>
public static class TasksEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tasks")
            .AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapPost("/{taskId:guid}/cancel", CancelAsync).WithName("CancelTask");

        return app;
    }

    private static async Task<IResult> CancelAsync(
        Guid taskId,
        TaskQueueService service,
        CancellationToken cancellationToken)
        => RunnerEndpoints.MapResult(await service.CancelAsync(taskId, cancellationToken));
}
