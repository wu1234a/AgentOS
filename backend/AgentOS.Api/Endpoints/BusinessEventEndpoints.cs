using AgentOS.Api.Business;
using AgentOS.Api.Filters;
using AgentOS.Application.Events;

namespace AgentOS.Api.Endpoints;

public static class BusinessEventEndpoints
{
    public static IEndpointRouteBuilder MapBusinessEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/business-events", SubmitAsync)
            .AddEndpointFilter<ApiClientAuthenticationFilter>();
        return app;
    }

    private static async Task<IResult> SubmitAsync(
        BusinessEventRequest request,
        BusinessEventService service,
        CancellationToken cancellationToken)
    {
        var (result, error) = await service.SubmitAsync(request, cancellationToken);
        if (result is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Business event rejected",
                extensions: new Dictionary<string, object?> { ["code"] = error });
        }

        return result.Outcome switch
        {
            CreateEventOutcome.Created => Results.Accepted(value: new { eventId = result.EventId, status = "accepted", tasksCreated = result.TasksCreated }),
            CreateEventOutcome.Duplicate => Results.Ok(new { eventId = result.ExistingEventId, status = "duplicate" }),
            CreateEventOutcome.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Business event conflicts with an existing dedupe key"),
            _ => Results.ValidationProblem(result.Errors)
        };
    }
}
