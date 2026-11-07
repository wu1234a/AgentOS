using AgentOS.Api.Filters;
using AgentOS.Application.Approvals;

namespace AgentOS.Api.Endpoints;

public static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/approval-requests").AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapGet("", ListAsync).WithName("ListApprovalRequests");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetApprovalRequest");
        group.MapPost("", CreateAsync).WithName("CreateApprovalRequest");
        group.MapPost("/{id:guid}/decision", DecideAsync).WithName("DecideApprovalRequest");
        return app;
    }

    private static async Task<IResult> ListAsync(
        ApprovalService service,
        string? status,
        string? targetType,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var (items, total) = await service.ListAsync(
            status,
            targetType,
            Math.Clamp(limit ?? 50, 1, 100),
            Math.Max(0, offset ?? 0),
            cancellationToken);
        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ApprovalService service,
        CancellationToken cancellationToken)
    {
        var approval = await service.GetAsync(id, cancellationToken);
        return approval is null ? Results.NotFound() : Results.Ok(approval);
    }

    private static async Task<IResult> CreateAsync(
        CreateApprovalRequest request,
        ApprovalService service,
        CancellationToken cancellationToken)
        => Map(await service.CreateAsync(request, cancellationToken));

    private static async Task<IResult> DecideAsync(
        Guid id,
        ApprovalDecisionRequest request,
        ApprovalService service,
        CancellationToken cancellationToken)
        => Map(await service.DecideAsync(id, request, cancellationToken));

    private static IResult Map(ApprovalResult result)
        => result.Outcome switch
        {
            ApprovalOutcome.Ok => Results.Ok(result.Approval),
            ApprovalOutcome.NotFound => Results.NotFound(),
            ApprovalOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Approval conflict",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            _ => Results.ValidationProblem(result.Errors)
        };
}
