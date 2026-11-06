using AgentOS.Application.Tasks;
using AgentOS.Domain.Workers;

namespace AgentOS.Api.Filters;

/// <summary>
/// Resolves the calling Runner from the <c>X-AgentOS-Worker-Code</c> header. AOS-005 uses the
/// development API key plus a registered worker code; AOS-006 replaces this with per-Runner
/// credentials. A normal API client cannot act as a Runner without a registered worker identity.
/// </summary>
public sealed class RunnerAuthenticationFilter(ITaskQueueStore store) : IEndpointFilter
{
    public const string WorkerIdItemKey = "WorkerId";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var workerCode = context.HttpContext.Request.Headers["X-AgentOS-Worker-Code"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(workerCode))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Runner identity required",
                detail: "The X-AgentOS-Worker-Code header is required on runner endpoints.");
        }

        var worker = await store.FindWorkerByCodeAsync(workerCode.Trim(), context.HttpContext.RequestAborted);

        if (worker is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unknown runner",
                detail: "The supplied worker code is not registered.");
        }

        if (worker.Status != WorkerStatus.Online)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Runner is not available",
                detail: $"Worker '{worker.WorkerCode}' is {worker.Status}.");
        }

        context.HttpContext.Items[WorkerIdItemKey] = worker.Id;
        return await next(context);
    }
}
