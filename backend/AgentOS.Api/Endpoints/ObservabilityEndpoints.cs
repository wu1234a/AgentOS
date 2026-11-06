using AgentOS.Api.Filters;
using AgentOS.Application.Observability;

namespace AgentOS.Api.Endpoints;

/// <summary>Read and artifact endpoints for the management UI (AOS-008).</summary>
public static class ObservabilityEndpoints
{
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapObservabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapGet("/dashboard/stats", GetDashboardAsync).WithName("GetDashboardStats");
        group.MapGet("/events/{eventId:guid}/tasks", GetEventTasksAsync).WithName("GetEventTasks");
        group.MapGet("/tasks", ListTasksAsync).WithName("ListTasks");
        group.MapGet("/tasks/{taskId:guid}", GetTaskAsync).WithName("GetTask");
        group.MapGet("/tasks/{taskId:guid}/attempts/{attemptId:guid}/logs", GetAttemptLogsAsync)
            .WithName("GetAttemptLogs");
        group.MapGet("/workers", ListWorkersAsync).WithName("ListWorkers");
        group.MapGet("/artifacts/{artifactId:guid}/download", DownloadArtifactAsync).WithName("DownloadArtifact");

        return app;
    }

    private static async Task<IResult> GetDashboardAsync(
        IObservabilityQueryStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
        => Results.Ok(await store.GetDashboardAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken));

    private static async Task<IResult> GetEventTasksAsync(
        Guid eventId,
        IObservabilityQueryStore store,
        CancellationToken cancellationToken)
    {
        var detail = await store.GetEventAsync(eventId, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(new { items = detail.Tasks });
    }

    private static async Task<IResult> ListTasksAsync(
        IObservabilityQueryStore store,
        string? status,
        Guid? projectId,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var page = Normalize(limit, offset);
        var (items, total) = await store.ListTasksAsync(status, projectId, page.Limit, page.Offset, cancellationToken);
        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> GetTaskAsync(
        Guid taskId,
        IObservabilityQueryStore store,
        CancellationToken cancellationToken)
    {
        var detail = await store.GetTaskAsync(taskId, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> GetAttemptLogsAsync(
        Guid taskId,
        Guid attemptId,
        IObservabilityQueryStore store,
        string? level,
        int? afterSequence,
        int? limit,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(limit ?? 100, 1, 500);
        var page = await store.GetAttemptLogsAsync(
            taskId,
            attemptId,
            level,
            Math.Max(0, afterSequence ?? 0),
            pageSize,
            cancellationToken);
        return Results.Ok(page);
    }

    private static async Task<IResult> ListWorkersAsync(
        IObservabilityQueryStore store,
        CancellationToken cancellationToken)
        => Results.Ok(new { items = await store.ListWorkersAsync(cancellationToken) });

    private static async Task<IResult> DownloadArtifactAsync(
        Guid artifactId,
        IArtifactStore store,
        CancellationToken cancellationToken)
    {
        var opened = await store.OpenAsync(artifactId, cancellationToken);
        if (opened is null)
        {
            return Results.NotFound();
        }

        return Results.File(
            opened.Value.Stream,
            opened.Value.Artifact.MediaType,
            opened.Value.Artifact.Name);
    }

    private static (int Limit, int Offset) Normalize(int? limit, int? offset)
        => (Math.Clamp(limit ?? 50, 1, MaxPageSize), Math.Max(0, offset ?? 0));
}
