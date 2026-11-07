using AgentOS.Api.Filters;
using AgentOS.Application.Projects;

namespace AgentOS.Api.Endpoints;

public static class ProjectsEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/projects").AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPatch("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/enable", EnableAsync);
        group.MapPost("/{id:guid}/disable", DisableAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(ProjectService service, CancellationToken cancellationToken)
        => Results.Ok(new { items = await service.ListAsync(cancellationToken) });

    private static async Task<IResult> GetAsync(Guid id, ProjectService service, CancellationToken cancellationToken)
    {
        var project = await service.GetAsync(id, cancellationToken);
        return project is null ? Results.NotFound() : Results.Ok(project);
    }

    private static async Task<IResult> CreateAsync(CreateProjectRequest request, ProjectService service, CancellationToken cancellationToken)
        => Map(await service.CreateAsync(request, cancellationToken));

    private static async Task<IResult> UpdateAsync(Guid id, UpdateProjectRequest request, ProjectService service, CancellationToken cancellationToken)
        => Map(await service.UpdateAsync(id, request, cancellationToken));

    private static async Task<IResult> EnableAsync(Guid id, ProjectService service, CancellationToken cancellationToken)
        => Map(await service.SetEnabledAsync(id, true, cancellationToken));

    private static async Task<IResult> DisableAsync(Guid id, ProjectService service, CancellationToken cancellationToken)
        => Map(await service.SetEnabledAsync(id, false, cancellationToken));

    private static IResult Map(ProjectResult result)
        => result.Outcome switch
        {
            ProjectOutcome.Ok => Results.Ok(result.Project),
            ProjectOutcome.NotFound => Results.NotFound(),
            ProjectOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Project conflict",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            _ => Results.ValidationProblem(result.Errors)
        };
}
