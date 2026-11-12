using AgentOS.Api.Filters;
using AgentOS.Api.ProjectPlanning;

namespace AgentOS.Api.Endpoints;

public static class ProjectPlanEndpoints
{
    public static IEndpointRouteBuilder MapProjectPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/project-plans")
            .AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapGet("", ListAsync);
        group.MapGet("/{planId:guid}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPost("/{planId:guid}/approve", ApproveAsync);
        group.MapPost("/{planId:guid}/reject", RejectAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        string? status,
        int? limit,
        ProjectPlanService service,
        CancellationToken cancellationToken)
        => Results.Ok(await service.ListAsync(status, limit ?? 20, cancellationToken));

    private static async Task<IResult> GetAsync(
        Guid planId,
        ProjectPlanService service,
        CancellationToken cancellationToken)
    {
        var plan = await service.GetAsync(planId, cancellationToken);
        return plan is null ? Results.NotFound() : Results.Ok(plan);
    }

    private static async Task<IResult> CreateAsync(
        CreateProjectPlanRequest request,
        ProjectPlanService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.Success
            ? Results.Accepted($"/api/v1/project-plans/{result.Plan!.Id}", result.Plan)
            : Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid project plan",
                detail: result.ErrorMessage,
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode });
    }

    private static async Task<IResult> ApproveAsync(
        Guid planId,
        ProjectPlanService service,
        CancellationToken cancellationToken)
        => MapDecision(await service.ApproveAsync(planId, cancellationToken));

    private static async Task<IResult> RejectAsync(
        Guid planId,
        ProjectPlanService service,
        CancellationToken cancellationToken)
        => MapDecision(await service.RejectAsync(planId, cancellationToken));

    private static IResult MapDecision(ProjectPlanApprovalResult result)
    {
        if (result.Success)
        {
            return Results.Ok(result.Plan);
        }

        var status = result.ErrorCode == "plan-not-found"
            ? StatusCodes.Status404NotFound
            : result.ErrorCode == "plan-not-pending"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
        return Results.Problem(
            statusCode: status,
            title: "Project plan decision failed",
            detail: result.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode });
    }
}
