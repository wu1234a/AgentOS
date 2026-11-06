using AgentOS.Api.Filters;
using AgentOS.Api.Realtime;
using AgentOS.Application.Observability;
using AgentOS.Application.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace AgentOS.Api.Endpoints;

/// <summary>Runner-facing task queue endpoints (AOS-005) and runner session/logs (AOS-006).</summary>
public static class RunnerEndpoints
{
    private const int MaxClaimCapacity = 100;
    private const int MaxLogEntriesPerBatch = 200;
    private const int MaxLogMessageLength = 4000;

    public static IEndpointRouteBuilder MapRunnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/runners")
            .AddEndpointFilter<RunnerAuthenticationFilter>();

        group.MapPost("/claim", ClaimAsync).WithName("ClaimTasks");
        group.MapPost("/heartbeat", WorkerHeartbeatAsync).WithName("RunnerHeartbeat");
        group.MapPost("/tasks/{taskId:guid}/start", StartAsync).WithName("StartTask");
        group.MapPost("/tasks/{taskId:guid}/heartbeat", HeartbeatAsync).WithName("HeartbeatTask");
        group.MapPost("/tasks/{taskId:guid}/complete", CompleteAsync).WithName("CompleteTask");
        group.MapPost("/tasks/{taskId:guid}/fail", FailAsync).WithName("FailTask");
        group.MapPost("/tasks/{taskId:guid}/logs", AppendLogsAsync).WithName("AppendTaskLogs");
        group.MapPost("/tasks/{taskId:guid}/artifacts", UploadArtifactAsync).WithName("UploadTaskArtifact");

        return app;
    }

    private static async Task<IResult> WorkerHeartbeatAsync(
        HttpContext httpContext,
        ITaskQueueStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var workerId = (Guid)httpContext.Items[RunnerAuthenticationFilter.WorkerIdItemKey]!;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (!await store.TouchWorkerAsync(workerId, now, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Worker not found");
        }

        return Results.Ok(new { workerId, serverTimeUtc = now });
    }

    private static async Task<IResult> AppendLogsAsync(
        Guid taskId,
        RunnerLogsRequest request,
        HttpContext httpContext,
        TaskQueueService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetLease(request.AttemptId, request.LeaseToken, out var attemptId, out var token, out var leaseError))
        {
            return leaseError!;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var entries = new List<TaskLogEntry>();

        if (request.Entries is null || request.Entries.Count == 0)
        {
            errors["entries"] = ["entries must contain at least one log line."];
        }
        else if (request.Entries.Count > MaxLogEntriesPerBatch)
        {
            errors["entries"] = [$"entries must contain at most {MaxLogEntriesPerBatch} lines."];
        }
        else
        {
            foreach (var entry in request.Entries)
            {
                if (entry.Sequence <= 0)
                {
                    errors["entries"] = ["every entry needs a positive sequence."];
                    break;
                }

                if (string.IsNullOrWhiteSpace(entry.Message) || entry.Message.Length > MaxLogMessageLength)
                {
                    errors["entries"] = [$"every message must be 1-{MaxLogMessageLength} characters."];
                    break;
                }

                entries.Add(new TaskLogEntry(entry.Sequence, entry.Level, entry.Message, entry.TimestampUtc));
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var workerId = (Guid)httpContext.Items[RunnerAuthenticationFilter.WorkerIdItemKey]!;
        var result = await service.AppendLogsAsync(
            workerId,
            taskId,
            attemptId,
            token,
            entries,
            cancellationToken);

        return MapResult(result);
    }

    private static async Task<IResult> ClaimAsync(
        ClaimTasksRequest? request,
        HttpContext httpContext,
        ITaskClaimer claimer,
        IHubContext<TaskHub> hub,
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
        await TaskEventPublisher.PublishClaimAsync(hub, claimed, cancellationToken);

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
        IHubContext<TaskHub> hub,
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

        await TaskEventPublisher.PublishAsync(hub, result, cancellationToken);
        return MapResult(result);
    }

    private static async Task<IResult> FailAsync(
        Guid taskId,
        FailTaskRequest request,
        HttpContext httpContext,
        TaskQueueService service,
        IHubContext<TaskHub> hub,
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

        await TaskEventPublisher.PublishAsync(hub, result, cancellationToken);
        return MapResult(result);
    }

    private static async Task<IResult> UploadArtifactAsync(
        Guid taskId,
        HttpContext httpContext,
        TaskQueueService queue,
        IArtifactStore artifacts,
        string? attemptId,
        string? name,
        string? mediaType,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(attemptId, out var resolvedAttemptId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["attemptId"] = ["attemptId must be a GUID."]
            });
        }

        var leaseToken = httpContext.Request.Headers["X-AgentOS-Lease-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(leaseToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["leaseToken"] = ["The X-AgentOS-Lease-Token header is required."]
            });
        }

        var workerId = (Guid)httpContext.Items[RunnerAuthenticationFilter.WorkerIdItemKey]!;
        var authorized = await queue.AuthorizeLeaseAsync(
            workerId,
            taskId,
            resolvedAttemptId,
            leaseToken,
            cancellationToken);

        if (authorized.Outcome != TaskQueueOutcome.Ok)
        {
            return MapResult(authorized);
        }

        try
        {
            var artifact = await artifacts.SaveAsync(
                authorized.Task!.ProjectId,
                taskId,
                resolvedAttemptId,
                workerId,
                string.IsNullOrWhiteSpace(name) ? "artifact" : name,
                string.IsNullOrWhiteSpace(mediaType) ? "application/octet-stream" : mediaType,
                httpContext.Request.Body,
                cancellationToken);

            return Results.Created(
                $"/api/v1/artifacts/{artifact.Id}/download",
                new ArtifactView(
                    artifact.Id,
                    artifact.Name,
                    artifact.MediaType,
                    artifact.SizeBytes,
                    artifact.Sha256,
                    artifact.CreatedAtUtc));
        }
        catch (ArtifactValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["artifact"] = [exception.Message]
            });
        }

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
