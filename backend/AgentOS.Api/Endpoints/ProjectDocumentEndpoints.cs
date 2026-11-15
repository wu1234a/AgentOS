using AgentOS.Api.Filters;
using AgentOS.Api.ProjectDocuments;

namespace AgentOS.Api.Endpoints;

public static class ProjectDocumentEndpoints
{
    public static IEndpointRouteBuilder MapProjectDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/projects/{projectId:guid}/documents")
            .AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapGet("", ListAsync);
        group.MapGet("/{documentId:guid}", GetAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid projectId,
        ProjectDocumentService service,
        CancellationToken cancellationToken)
        => Results.Ok(await service.ListAsync(projectId, cancellationToken));

    private static async Task<IResult> GetAsync(
        Guid projectId,
        Guid documentId,
        ProjectDocumentService service,
        CancellationToken cancellationToken)
    {
        var document = await service.GetAsync(projectId, documentId, cancellationToken);
        return document is null ? Results.NotFound() : Results.Ok(document);
    }
}
