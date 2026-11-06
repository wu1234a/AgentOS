using AgentOS.Api.Filters;
using AgentOS.Application.Planning;

namespace AgentOS.Api.Endpoints;

/// <summary>Planner agent draft endpoints (AOS-012).</summary>
public static class PlannerEndpoints
{
    public static IEndpointRouteBuilder MapPlannerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/planner/drafts").AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapGet("", ListAsync).WithName("ListPlannerDrafts");
        group.MapGet("/{draftId:guid}", GetAsync).WithName("GetPlannerDraft");
        group.MapPost("", CreateAsync).WithName("CreatePlannerDraft");
        group.MapPost("/{draftId:guid}/approve", ApproveAsync).WithName("ApprovePlannerDraft");
        group.MapPost("/{draftId:guid}/reject", RejectAsync).WithName("RejectPlannerDraft");

        return app;
    }

    private static async Task<IResult> ListAsync(
        PlannerService service,
        string? status,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var (items, total) = await service.ListAsync(
            status,
            Math.Clamp(limit ?? 50, 1, 200),
            Math.Max(0, offset ?? 0),
            cancellationToken);

        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> GetAsync(
        Guid draftId,
        PlannerService service,
        CancellationToken cancellationToken)
    {
        var draft = await service.GetAsync(draftId, cancellationToken);
        return draft is null ? Results.NotFound() : Results.Ok(draft);
    }

    private static async Task<IResult> CreateAsync(
        CreatePlanningDraftRequest request,
        PlannerService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.Outcome == PlannerOutcome.Ok
            ? Results.Created($"/api/v1/planner/drafts/{result.Draft!.Id}", result.Draft)
            : Map(result);
    }

    private static async Task<IResult> ApproveAsync(
        Guid draftId,
        PlannerService service,
        CancellationToken cancellationToken)
        => Map(await service.ApproveAsync(draftId, cancellationToken));

    private static async Task<IResult> RejectAsync(
        Guid draftId,
        PlannerService service,
        CancellationToken cancellationToken)
        => Map(await service.RejectAsync(draftId, cancellationToken));

    private static IResult Map(PlannerResult result)
        => result.Outcome switch
        {
            PlannerOutcome.Ok => Results.Ok(result.Draft),
            PlannerOutcome.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Planner draft not found"),
            PlannerOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Planner draft already decided",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            PlannerOutcome.ProviderError => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Planner provider failed",
                detail: result.ProviderDetail,
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            PlannerOutcome.NotConfigured => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Planner is not configured",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            _ => Results.ValidationProblem(result.Errors)
        };
}
