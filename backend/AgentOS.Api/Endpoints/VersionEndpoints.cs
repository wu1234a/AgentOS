using AgentOS.Api;

namespace AgentOS.Api.Endpoints;

public static class VersionEndpoints
{
    public static IEndpointRouteBuilder MapVersionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/version", () => Results.Ok(BuildMetadata.Get()))
            .WithName("GetVersion");
        return app;
    }
}
