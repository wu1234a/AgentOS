using AgentOS.Api.Filters;
using AgentOS.Api.Knowledge;

namespace AgentOS.Api.Endpoints;

public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/knowledge").AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapGet("", SearchAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        return app;
    }

    private static async Task<IResult> SearchAsync(
        Guid? projectId, string? query, int? limit, KnowledgeService service, CancellationToken cancellationToken)
        => Results.Ok(new { items = await service.SearchAsync(projectId, query, limit ?? 20, cancellationToken) });

    private static async Task<IResult> GetAsync(Guid id, KnowledgeService service, CancellationToken cancellationToken)
    {
        var document = await service.GetAsync(id, cancellationToken);
        return document is null ? Results.NotFound() : Results.Ok(document);
    }

    private static async Task<IResult> CreateAsync(
        KnowledgeDocumentRequest request, KnowledgeService service, CancellationToken cancellationToken)
    {
        var (document, error) = await service.CreateAsync(request, cancellationToken);
        return document is null
            ? Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Knowledge document invalid",
                extensions: new Dictionary<string, object?> { ["code"] = error })
            : Results.Ok(document);
    }

    private static async Task<IResult> DeleteAsync(Guid id, KnowledgeService service, CancellationToken cancellationToken)
        => await service.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
}
