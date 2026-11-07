using AgentOS.Api.Filters;
using AgentOS.Api.Pipelines;

namespace AgentOS.Api.Endpoints;

public static class PipelineEndpoints
{
    public static IEndpointRouteBuilder MapPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pipelines").AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapGet("", ListAsync);
        group.MapGet("/tasks/{taskId:guid}", GetAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid? projectId, int? limit, PipelineService service, CancellationToken cancellationToken)
        => Results.Ok(new { items = await service.ListAsync(projectId, limit ?? 20, cancellationToken) });

    private static async Task<IResult> GetAsync(Guid taskId, PipelineService service, CancellationToken cancellationToken)
    {
        var pipeline = await service.GetAsync(taskId, cancellationToken);
        return pipeline is null ? Results.NotFound() : Results.Ok(pipeline);
    }
}
