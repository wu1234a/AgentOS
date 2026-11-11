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
        group.MapPost("/{itemId:guid}/cancel", CancelAsync);
        return app;
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
