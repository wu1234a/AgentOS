using AgentOS.Application.Security;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Security;
using AgentOS.Domain.Workers;

namespace AgentOS.Api.Filters;

/// <summary>
/// Authenticates a Runner either with a runner-scoped client credential
/// (<c>X-AgentOS-Client-Id</c> + <c>X-AgentOS-Client-Secret</c>) or, in development, with the
/// shared dev key plus a registered <c>X-AgentOS-Worker-Code</c>. A plain API client cannot act
/// as a Runner without a registered worker identity.
/// </summary>
public sealed class RunnerAuthenticationFilter(
    IApiClientAuthenticator devAuthenticator,
    ITaskQueueStore queueStore,
    IApiClientStore clientStore,
    TimeProvider timeProvider) : IEndpointFilter
{
    public const string WorkerIdItemKey = "WorkerId";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var clientId = httpContext.Request.Headers["X-AgentOS-Client-Id"].FirstOrDefault();
        var clientSecret = httpContext.Request.Headers["X-AgentOS-Client-Secret"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(clientId) || !string.IsNullOrWhiteSpace(clientSecret))
        {
            var clientFailure = await AuthenticateClientAsync(httpContext, clientId, clientSecret);
            return clientFailure ?? await next(context);
        }

        var devFailure = await AuthenticateDevelopmentRunnerAsync(httpContext);
        return devFailure ?? await next(context);
    }

    private async Task<object?> AuthenticateClientAsync(
        HttpContext httpContext,
        string? clientId,
        string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return Unauthorized("Runner client id and secret are both required.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var client = await clientStore.FindByCodeAsync(clientId.Trim(), httpContext.RequestAborted);

        if (client is null
            || client.ClientType != ApiClientType.Runner
            || !client.IsEnabled
            || client.WorkerId is null
            || (client.ExpiresAtUtc is not null && client.ExpiresAtUtc <= now)
            || !ClientSecretHasher.Matches(client.SecretHash, clientSecret))
        {
            return Unauthorized("Runner credentials are invalid.");
        }

        var worker = await queueStore.FindWorkerByIdAsync(client.WorkerId.Value, httpContext.RequestAborted);
        if (worker is null)
        {
            return Unauthorized("The runner is not bound to a registered worker.");
        }

        if (worker.Status == WorkerStatus.Disabled
            && httpContext.Request.Path.Value?.EndsWith("/claim", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Runner is disabled",
                detail: $"Worker '{worker.WorkerCode}' is disabled for new claims.");
        }

        await clientStore.TouchAsync(client.Id, now, httpContext.RequestAborted);
        httpContext.Items[WorkerIdItemKey] = client.WorkerId.Value;
        return null;
    }

    private async Task<object?> AuthenticateDevelopmentRunnerAsync(HttpContext httpContext)
    {
        var devKey = httpContext.Request.Headers["X-AgentOS-Dev-Key"].FirstOrDefault();
        var identity = await devAuthenticator.AuthenticateAsync(devKey, httpContext.RequestAborted);
        if (identity is null)
        {
            return Unauthorized("A runner credential or development key is required.");
        }

        var workerCode = httpContext.Request.Headers["X-AgentOS-Worker-Code"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(workerCode))
        {
            return Unauthorized("The X-AgentOS-Worker-Code header is required in development mode.");
        }

        var worker = await queueStore.FindWorkerByCodeAsync(workerCode.Trim(), httpContext.RequestAborted);
        if (worker is null)
        {
            return Unauthorized("The supplied worker code is not registered.");
        }

        if (worker.Status != WorkerStatus.Online)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Runner is not available",
                detail: $"Worker '{worker.WorkerCode}' is {worker.Status}.");
        }

        httpContext.Items[WorkerIdItemKey] = worker.Id;
        return null;
    }

    private static IResult Unauthorized(string detail)
        => Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Runner authentication failed",
            detail: detail);
}
