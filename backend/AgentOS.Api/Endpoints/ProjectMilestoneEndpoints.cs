using AgentOS.Api.Filters;
using AgentOS.Api.ProjectDocuments;

namespace AgentOS.Api.Endpoints;

public static class ProjectMilestoneEndpoints
{
    public static IEndpointRouteBuilder MapProjectMilestoneEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/projects/{projectId:guid}")
            .AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapGet("/milestones", ListAsync);
        group.MapPost("/milestones/{milestoneId:guid}/verify", VerifyAsync);
        group.MapPost("/accept", AcceptAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid projectId,
        ProjectMilestoneService service,
        CancellationToken cancellationToken)
        => Results.Ok(await service.ListAsync(projectId, cancellationToken));

    private static async Task<IResult> VerifyAsync(
        Guid projectId,
        Guid milestoneId,
        HttpRequest request,
        ProjectMilestoneService service,
        CancellationToken cancellationToken)
    {
        var milestone = await service.VerifyAsync(projectId, milestoneId, Actor(request), cancellationToken);
        return milestone is null ? Results.NotFound() : Results.Ok(milestone);
    }

    private static async Task<IResult> AcceptAsync(
        Guid projectId,
        HttpRequest request,
        ProjectMilestoneService service,
        CancellationToken cancellationToken)
    {
        var result = await service.AcceptAsync(projectId, Actor(request), cancellationToken);
        if (result.Success)
        {
            return Results.Ok(new { status = "Completed" });
        }

        return Results.Problem(
            statusCode: result.ErrorCode == "project-not-found"
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict,
            title: "Project acceptance failed",
            detail: result.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode });
    }

    private static string Actor(HttpRequest request)
    {
        var value = request.Headers["X-AgentOS-Actor"].FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? "api-key" : value;
    }
}
