using AgentOS.Api.Backlog;
using AgentOS.Api.Filters;

namespace AgentOS.Api.Endpoints;

public static class BacklogEndpoints
{
    public static IEndpointRouteBuilder MapBacklogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backlog").AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPost("/generate", GenerateAsync);
        group.MapPost("/{itemId:guid}/cancel", CancelAsync);
        return app;
    }

    private static async Task<IResult> GenerateAsync(
        GenerateBacklogRequest request,
        BacklogPlannerService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GenerateAsync(request, cancellationToken);
        if (result.Success)
        {
            return Results.Ok(new { items = result.Items });
        }

        return Results.Problem(
            statusCode: result.ErrorCode is "invalid-request" ? StatusCodes.Status400BadRequest : StatusCodes.Status502BadGateway,
            title: "Backlog generation failed",
            detail: result.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode });
    }

    private static async Task<IResult> ListAsync(
        string? status,
        int? limit,
        BacklogService service,
        CancellationToken cancellationToken)
        => Results.Ok(await service.ListAsync(status, limit ?? 50, cancellationToken));

    private static async Task<IResult> CreateAsync(
        CreateBacklogItemRequest request,
        BacklogService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.Success
            ? Results.Created($"/api/v1/backlog/{result.Item!.Id}", result.Item)
            : Results.ValidationProblem(result.Errors);
    }

    private static async Task<IResult> CancelAsync(
        Guid itemId,
        BacklogService service,
        CancellationToken cancellationToken)
        => await service.CancelAsync(itemId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
}
