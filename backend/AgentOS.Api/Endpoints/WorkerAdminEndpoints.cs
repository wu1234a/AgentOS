using AgentOS.Api.Filters;
using AgentOS.Api.Workers;

namespace AgentOS.Api.Endpoints;

public static class WorkerAdminEndpoints
{
    public static IEndpointRouteBuilder MapWorkerAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workers").AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapGet("", ListAsync);
        group.MapGet("/stats", StatsAsync);
        group.MapPost("/{id:guid}/enable", EnableAsync);
        group.MapPost("/{id:guid}/disable", DisableAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(string? status, WorkerAdminService service, CancellationToken cancellationToken)
        => Results.Ok(new { items = await service.ListAsync(status, cancellationToken) });

    private static async Task<IResult> StatsAsync(WorkerAdminService service, CancellationToken cancellationToken)
        => Results.Ok(await service.StatsAsync(cancellationToken));

    private static async Task<IResult> EnableAsync(Guid id, WorkerAdminService service, CancellationToken cancellationToken)
        => await service.SetEnabledAsync(id, true, cancellationToken) ? Results.Ok() : Results.NotFound();

    private static async Task<IResult> DisableAsync(Guid id, WorkerAdminService service, CancellationToken cancellationToken)
        => await service.SetEnabledAsync(id, false, cancellationToken) ? Results.Ok() : Results.NotFound();
}
