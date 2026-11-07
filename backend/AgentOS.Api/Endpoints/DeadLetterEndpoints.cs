using AgentOS.Api.Filters;
using AgentOS.Api.Operations;

namespace AgentOS.Api.Endpoints;

public static class DeadLetterEndpoints
{
    public static IEndpointRouteBuilder MapDeadLetterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dead-letters").AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapGet("", ListAsync);
        group.MapPost("/{id:guid}/retry", RetryAsync);
        group.MapPost("/{id:guid}/takeover", RetryAsync);
        group.MapPost("/{id:guid}/cancel", CancelAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid? projectId,
        string? status,
        int? limit,
        DeadLetterService service,
        CancellationToken cancellationToken)
        => Results.Ok(new { items = await service.ListAsync(projectId, status, limit ?? 50, cancellationToken) });

    private static async Task<IResult> RetryAsync(Guid id, DeadLetterService service, CancellationToken cancellationToken)
        => await service.RetryAsync(id, cancellationToken)
            ? Results.Ok(new { status = "requeued" })
            : Results.NotFound();

    private static async Task<IResult> CancelAsync(Guid id, DeadLetterService service, CancellationToken cancellationToken)
        => await service.CancelAsync(id, cancellationToken)
            ? Results.Ok(new { status = "cancelled" })
            : Results.NotFound();
}
