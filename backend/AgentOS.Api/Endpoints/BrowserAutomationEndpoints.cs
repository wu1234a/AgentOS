using AgentOS.Api.Browser;
using AgentOS.Api.Filters;

namespace AgentOS.Api.Endpoints;

public static class BrowserAutomationEndpoints
{
    public static IEndpointRouteBuilder MapBrowserAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/browser/execute", ExecuteAsync)
            .AddEndpointFilter<ApiClientAuthenticationFilter>();
        return app;
    }

    private static async Task<IResult> ExecuteAsync(
        BrowserActionRequest request,
        BrowserAutomationService service,
        CancellationToken cancellationToken)
    {
        var (result, error) = await service.ExecuteAsync(request, cancellationToken);
        return result is null
            ? Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Browser action failed",
                extensions: new Dictionary<string, object?> { ["code"] = error })
            : Results.Ok(result);
    }
}
