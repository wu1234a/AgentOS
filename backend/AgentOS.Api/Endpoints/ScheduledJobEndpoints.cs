using AgentOS.Api.Filters;
using AgentOS.Application.Scheduling;

namespace AgentOS.Api.Endpoints;

public static class ScheduledJobEndpoints
{
    public static IEndpointRouteBuilder MapScheduledJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/scheduled-jobs").AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPatch("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/enable", EnableAsync);
        group.MapPost("/{id:guid}/disable", DisableAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(ScheduledJobService service, CancellationToken cancellationToken)
        => Results.Ok(new { items = await service.ListAsync(cancellationToken) });

    private static async Task<IResult> GetAsync(Guid id, ScheduledJobService service, CancellationToken cancellationToken)
    {
        var job = await service.GetAsync(id, cancellationToken);
        return job is null ? Results.NotFound() : Results.Ok(job);
    }

    private static async Task<IResult> CreateAsync(
        CreateScheduledJobRequest request,
        ScheduledJobService service,
        CancellationToken cancellationToken)
        => Map(await service.CreateAsync(request, cancellationToken));

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateScheduledJobRequest request,
        ScheduledJobService service,
        CancellationToken cancellationToken)
        => Map(await service.UpdateAsync(id, request, cancellationToken));

    private static async Task<IResult> DeleteAsync(Guid id, ScheduledJobService service, CancellationToken cancellationToken)
        => await service.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> EnableAsync(Guid id, ScheduledJobService service, CancellationToken cancellationToken)
        => Map(await service.SetEnabledAsync(id, true, cancellationToken));

    private static async Task<IResult> DisableAsync(Guid id, ScheduledJobService service, CancellationToken cancellationToken)
        => Map(await service.SetEnabledAsync(id, false, cancellationToken));

    private static IResult Map(ScheduleResult result)
        => result.Outcome switch
        {
            ScheduleOutcome.Ok => Results.Ok(result.Job),
            ScheduleOutcome.NotFound => Results.NotFound(),
            ScheduleOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Schedule conflict",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            _ => Results.ValidationProblem(result.Errors)
        };
}
