using AgentOS.Application.Security;

namespace AgentOS.Api.Filters;

/// <summary>
/// Requires a valid API client credential on the event endpoints. AOS-002 uses the
/// development key header; AOS-006 replaces the authenticator with per-client credentials.
/// </summary>
public sealed class ApiClientAuthenticationFilter(IApiClientAuthenticator authenticator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var apiKey = context.HttpContext.Request.Headers["X-AgentOS-Dev-Key"].FirstOrDefault();
        var identity = await authenticator.AuthenticateAsync(apiKey, context.HttpContext.RequestAborted);

        if (identity is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "A valid API client credential is required.");
        }

        context.HttpContext.Items["ApiClient"] = identity;
        return await next(context);
    }
}
