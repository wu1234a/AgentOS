using AgentOS.Api.Filters;
using AgentOS.Application.Tasks;

namespace AgentOS.Api.Endpoints;

/// <summary>Direct subtask creation/list with dependency summary (AOS-016b).</summary>
public static class SubtaskEndpoints
{
    public static IEndpointRouteBuilder MapSubtaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tasks/{parentTaskId:guid}/subtasks")
            .AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapPost("", CreateAsync).WithName("CreateSubtask");
        group.MapGet("", ListAsync).WithName("ListSubtasks");
        return app;
    }

    private static async Task<IResult> CreateAsync(
        Guid parentTaskId,
        CreateSubtaskRequest request,
        SubtaskService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(parentTaskId, request, cancellationToken);
        return result.Outcome switch
        {
            SubtaskOutcome.Ok => Results.Created($"/api/v1/tasks/{parentTaskId}/subtasks", result.Subtask),
            SubtaskOutcome.NotFound => Results.NotFound(),
            SubtaskOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Subtask conflict",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static async Task<IResult> ListAsync(
        Guid parentTaskId,
        SubtaskService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(parentTaskId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
