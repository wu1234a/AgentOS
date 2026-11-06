using AgentOS.Api.Filters;
using AgentOS.Application.Tasks;

namespace AgentOS.Api.Endpoints;

/// <summary>Runner-facing task queue endpoints introduced by AOS-005.</summary>
public static class RunnerEndpoints
{
    private const int MaxClaimCapacity = 100;

    public static IEndpointRouteBuilder MapRunnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/runners")
            .AddEndpointFilter<ApiClientAuthenticationFilter>()
            .AddEndpointFilter<RunnerAuthenticationFilter>();

        group.MapPost("/claim", ClaimAsync).WithName("ClaimTasks");
        group.MapPost("/tasks/{taskId:guid}/start", StartAsync).WithName("StartTask");
        group.MapPost("/tasks/{taskId:guid}/heartbeat", HeartbeatAsync).WithName("HeartbeatTask");
        group.MapPost("/tasks/{taskId:guid}/complete", CompleteAsync).WithName("CompleteTask");
        group.MapPost("/tasks/{taskId:guid}/fail", FailAsync).WithName("FailTask");

        return app;
    }

    private static async Task<IResult> ClaimAsync(
        ClaimTasksRequest? request,
        HttpContext httpContext,
        ITaskClaimer claimer,
        CancellationToken cancellationToken)
    {
        var capacity = request?.Capacity ?? 1;
        if (capacity is < 1 or > MaxClaimCapacity)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["capacity"] = [$"capacity must be between 1 and {MaxClaimCapacity}."]
            });
        }

        var workerId = (Guid)httpContext.Items[RunnerAuthenticationFilter.WorkerIdItemKey]!;
        var claimed = await claimer.ClaimAsync(workerId, capacity, cancellationToken);

        return Results.Ok(new { tasks = claimed });
    }

    private static async Task<IResult> StartAsync(
        Guid taskId,
        StartTaskRequest request,
        HttpContext httpContext,
        TaskQueueService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetLease(request.AttemptId, request.LeaseToken, out var attemptId, out var token, out var error))
        {
            return error!;
        }

        var workerId = (Guid)httpContext.Items[RunnerAuthenticationFilter.WorkerIdItemKey]!;
        var result = await service.StartAsync(
            workerId,
            taskId,
            attemptId,
            token,
            request.ExternalSessionId,
            cancellationToken);

        return MapResult(result);
    }

    private static async Task<IResult> HeartbeatAsync(
        Guid taskId,
        HeartbeatTaskRequest request,
        HttpContext httpContext,
        TaskQueueService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetLease(request.AttemptId, request.LeaseToken, out var attemptId, out var token, out var error))
        {
            return error!;
        }

        var workerId = (Guid)httpContext.Items[RunnerAuthenticationFilter.WorkerIdItemKey]!;
        var result = await service.HeartbeatAsync(workerId, taskId, attemptId, token, cancellationToken);

        return MapResult(result);
    }

    private static async Task<IResult> CompleteAsync(
        Guid taskId,
        CompleteTaskRequest request,
        HttpContext httpContext,
        TaskQueueService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetLease(request.AttemptId, request.LeaseToken, out var attemptId, out var token, out var error))
        {
            return error!;
        }

        var workerId = (Guid)httpContext.Items[RunnerAuthenticationFilter.WorkerIdItemKey]!;
        var result = await service.CompleteAsync(
            workerId,
            taskId,
            attemptId,
            token,
            request.ExitCode,
            cancellationToken);

        return MapResult(result);
    }

    private static async Task<IResult> FailAsync(
        Guid taskId,
        FailTaskRequest request,
        HttpContext httpContext,
        TaskQueueService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetLease(request.AttemptId, request.LeaseToken, out var attemptId, out var token, out var error))
        {
            return error!;
        }

        var workerId = (Guid)httpContext.Items[RunnerAuthenticationFilter.WorkerIdItemKey]!;
        var result = await service.FailAsync(
            workerId,
            taskId,
            attemptId,
            token,
            request.ErrorCode,
            request.ErrorMessage,
            request.Retryable,
            request.Cancelled,
            cancellationToken);

        return MapResult(result);
    }

    internal static IResult MapResult(TaskQueueResult result)
        => result.Outcome switch
        {
            TaskQueueOutcome.Ok => Results.Ok(new
            {
                taskId = result.Task!.Id,
                status = result.Task.Status.ToString(),
                attemptNumber = result.Attempt?.AttemptNumber,
                attemptStatus = result.Attempt?.Status.ToString(),
                cancelRequested = result.CancelRequested,
                leaseExpiresAtUtc = result.LeaseExpiresAtUtc,
                completedAtUtc = result.Task.CompletedAtUtc,
                errorCode = result.Task.ErrorCode
            }),

            TaskQueueOutcome.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Task not found",
                extensions: new Dictionary<string, object?> { ["code"] = result.Error }),

            TaskQueueOutcome.Forbidden => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Lease validation failed",
                extensions: new Dictionary<string, object?> { ["code"] = result.Error }),

            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Task cannot accept this operation",
                extensions: new Dictionary<string, object?> { ["code"] = result.Error })
        };

    private static bool TryGetLease(
        Guid? attemptId,
        string? leaseToken,
        out Guid resolvedAttemptId,
        out string resolvedToken,
        out IResult? error)
    {
        resolvedAttemptId = attemptId ?? Guid.Empty;
        resolvedToken = leaseToken?.Trim() ?? string.Empty;
        error = null;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (attemptId is null || attemptId == Guid.Empty)
        {
            errors["attemptId"] = ["attemptId is required."];
        }

        if (string.IsNullOrWhiteSpace(leaseToken))
        {
            errors["leaseToken"] = ["leaseToken is required."];
        }

        if (errors.Count > 0)
        {
            error = Results.ValidationProblem(errors);
            return false;
        }

        return true;
    }
}
