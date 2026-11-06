using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentOS.Application.Tasks;
using AgentOS.Domain.CodingRuns;
using AgentOS.Domain.Planning;
using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Application.CodingRuns;

/// <summary>
/// Creates a Coding Run from an approved Planner draft and projects the linked task's lifecycle
/// onto the run. The run owns the immutable snapshot; retries create a new task, never overwrite
/// the original attempt history.
/// </summary>
public sealed class CodingRunService(
    ICodingRunRepository repository,
    TaskQueueService taskQueue,
    TimeProvider timeProvider,
    ILogger<CodingRunService> logger)
{
    private const int MaxTitleLength = 200;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SyncLocks = new();

    public async Task<(IReadOnlyList<CodingRunView> Items, int Total)> ListAsync(
        string? status,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        // Project pending runs before filtering so list state is not stale after a Runner update.
        foreach (var pending in await repository.ListRunsForSyncAsync(500, cancellationToken))
        {
            try
            {
                await SyncAsync(pending, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not project coding run {RunId} while listing.", pending.Id);
            }
        }

        return await repository.ListAsync(status, limit, offset, cancellationToken);
    }

    public async Task<CodingRunDetail?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await repository.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        try
        {
            await SyncAsync(run, cancellationToken);
        }
        catch (Exception exception)
        {
            // Reads must not fail because another request committed the same projection first.
            logger.LogWarning(exception, "Could not project coding run {RunId} while reading.", run.Id);
        }

        var view = await GetViewAsync(run, cancellationToken);
        var events = await repository.ListEventsAsync(runId, cancellationToken);
        return new CodingRunDetail(view!, events);
    }

    public async Task<CodingRunResult> CreateAsync(
        CreateCodingRunRequest request,
        CancellationToken cancellationToken)
    {
        var draft = await repository.GetDraftAsync(request.DraftId, cancellationToken);
        if (draft is null)
        {
            return CodingRunResult.NotFound();
        }

        var validationCommands = NormalizeCommands(request.ValidationCommands);
        if (validationCommands is null)
        {
            return CodingRunResult.Invalid(
                "validation-commands-invalid",
                new Dictionary<string, string[]>
                {
                    ["validationCommands"] = ["At most 10 non-empty commands of up to 1000 characters are allowed."]
                });
        }

        if (draft.ProjectId is { } draftProjectId && draftProjectId != request.ProjectId)
        {
            return CodingRunResult.Invalid(
                "project-mismatch",
                new Dictionary<string, string[]>
                {
                    ["projectId"] = ["Project does not match the approved draft."]
                });
        }

        if (draft.Status != PlanningDraftStatus.Approved)
        {
            return CodingRunResult.Invalid(
                "draft-not-approved",
                new Dictionary<string, string[]>
                {
                    ["draftId"] = ["Only an approved Planner draft can create a Coding Run."]
                });
        }

        if (!await repository.ProjectEnabledAsync(request.ProjectId, cancellationToken))
        {
            return CodingRunResult.Invalid(
                "project-unavailable",
                new Dictionary<string, string[]>
                {
                    ["projectId"] = ["Project does not exist or is disabled."]
                });
        }

        if (!await repository.AgentEnabledAsync(request.AgentId, request.ProjectId, cancellationToken))
        {
            return CodingRunResult.Invalid(
                "agent-unavailable",
                new Dictionary<string, string[]>
                {
                    ["agentId"] = ["Agent does not exist, is disabled, or belongs to another project."]
                });
        }

        if (await repository.GetRunByDraftAsync(draft.Id, cancellationToken) is not null)
        {
            return CodingRunResult.Conflict("draft-already-has-run");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var runId = Guid.CreateVersion7();
        var hash = Hash(draft.ContentMarkdown);
        var snapshot = JsonSerializer.Serialize(new
        {
            codingRunId = runId,
            draftId = draft.Id,
            draftHash = hash,
            draftContent = draft.ContentMarkdown,
            projectId = request.ProjectId,
            agentId = request.AgentId,
            modelProfileId = draft.ModelProfileId,
            validationCommands,
            createdAtUtc = now
        });

        var run = new CodingRun
        {
            Id = runId,
            DraftId = draft.Id,
            DraftContentHash = hash,
            ProjectId = request.ProjectId,
            AgentId = request.AgentId,
            Status = CodingRunStatus.Queued,
            AttemptCount = 1,
            SnapshotJson = snapshot,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var task = CreateTask(run, draft, request, snapshot, now, 1);
        run.TaskId = task.Id;

        await repository.AddRunAsync(run, cancellationToken);
        await repository.AddTaskAsync(task, cancellationToken);
        await AddEventAsync(run, null, "created", snapshot, cancellationToken);
        await repository.SaveAsync(cancellationToken);

        logger.LogInformation(
            "Coding run {RunId} created for draft {DraftId} with task {TaskId}.",
            run.Id,
            draft.Id,
            task.Id);

        return CodingRunResult.Ok((await GetViewAsync(run, cancellationToken))!);
    }

    public async Task<CodingRunResult> CancelAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await repository.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            return CodingRunResult.NotFound();
        }

        if (IsTerminal(run.Status))
        {
            return CodingRunResult.Conflict("coding-run-terminal");
        }

        if (run.TaskId is { } taskId)
        {
            var result = await taskQueue.CancelAsync(taskId, cancellationToken);
            if (result.Outcome == TaskQueueOutcome.NotFound)
            {
                return CodingRunResult.Conflict("coding-run-task-missing");
            }
        }

        await SyncAsync(run, cancellationToken);
        return CodingRunResult.Ok((await GetViewAsync(run, cancellationToken))!);
    }

    public async Task<CodingRunResult> RetryAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await repository.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            return CodingRunResult.NotFound();
        }

        try
        {
            await SyncAsync(run, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not project coding run {RunId} before retry.", run.Id);
        }

        if (run.Status is not (CodingRunStatus.Failed or CodingRunStatus.Canceled))
        {
            return CodingRunResult.Conflict("coding-run-not-retryable");
        }

        if (!await repository.AgentEnabledAsync(run.AgentId, run.ProjectId, cancellationToken))
        {
            return CodingRunResult.Conflict("agent-unavailable");
        }

        var draft = await repository.GetDraftAsync(run.DraftId, cancellationToken);
        if (draft is null)
        {
            return CodingRunResult.Conflict("coding-run-draft-missing");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var attempt = run.AttemptCount + 1;
        var previousTask = run.TaskId is { } previousTaskId
            ? await repository.GetTaskAsync(previousTaskId, cancellationToken)
            : null;

        var request = new CreateCodingRunRequest
        {
            DraftId = run.DraftId,
            ProjectId = run.ProjectId,
            AgentId = run.AgentId,
            Priority = previousTask?.Priority,
            TimeoutSeconds = previousTask?.TimeoutSeconds
        };

        var task = CreateTask(run, draft, request, run.SnapshotJson, now, attempt);
        run.TaskId = task.Id;
        run.Status = CodingRunStatus.Queued;
        run.AttemptCount = attempt;
        run.ErrorCode = null;
        run.ErrorMessage = null;
        run.CompletedAtUtc = null;
        run.CancelRequestedAtUtc = null;
        run.UpdatedAtUtc = now;

        await repository.AddTaskAsync(task, cancellationToken);
        await AddEventAsync(run, null, "retrying", null, cancellationToken);
        await repository.SaveAsync(cancellationToken);

        return CodingRunResult.Ok((await GetViewAsync(run, cancellationToken))!);
    }

    private AutomationTask CreateTask(
        CodingRun run,
        PlanningDraft draft,
        CreateCodingRunRequest request,
        string snapshot,
        DateTime now,
        int attempt)
    {
        var taskId = Guid.CreateVersion7();
        var title = draft.Title.Length <= MaxTitleLength
            ? draft.Title
            : draft.Title[..MaxTitleLength];

        return new AutomationTask
        {
            Id = taskId,
            TaskCode = $"TASK-{taskId:N}".ToUpperInvariant(),
            ProjectId = request.ProjectId,
            AgentId = request.AgentId,
            // Keep the Planner API profile only in the snapshot; execution resolves the coding
            // agent/rule profile through AOS-010b and must not inherit an API-mode profile.
            ModelProfileId = null,
            LogicalTaskKey = $"coding-run:{run.Id}:{attempt}",
            Status = TaskStatus.Queued,
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? "normal" : request.Priority,
            Title = title,
            Description = $"Coding run {run.Id} from Planner draft {draft.Id}.",
            InputJson = snapshot,
            AttemptCount = 0,
            MaxAttempts = 2,
            TimeoutSeconds = Math.Clamp(request.TimeoutSeconds ?? 1800, 60, 7200),
            NextRunAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private async Task SyncAsync(CodingRun run, CancellationToken cancellationToken)
    {
        var gate = SyncLocks.GetOrAdd(run.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            await SyncCoreAsync(run, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SyncCoreAsync(CodingRun run, CancellationToken cancellationToken)
    {
        if (run.TaskId is not { } taskId)
        {
            return;
        }

        var task = await repository.GetTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return;
        }

        var next = task.Status == TaskStatus.Running && task.CancelRequestedAtUtc is not null
            ? CodingRunStatus.Canceling
            : Map(task.Status);

        if (task.CancelRequestedAtUtc is { } requestedAt && run.CancelRequestedAtUtc is null)
        {
            run.CancelRequestedAtUtc = requestedAt;
        }

        if (next == run.Status)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var previous = run.Status;

        if (next == CodingRunStatus.Running && run.StartedAtUtc is null)
        {
            run.StartedAtUtc = task.StartedAtUtc ?? now;
        }

        run.Status = next;
        run.UpdatedAtUtc = now;

        if (next is CodingRunStatus.Failed)
        {
            run.ErrorCode = task.ErrorCode;
            run.ErrorMessage = task.ErrorMessage;
            run.CompletedAtUtc = task.CompletedAtUtc ?? now;
        }
        else if (next is CodingRunStatus.Canceled)
        {
            run.CompletedAtUtc = task.CompletedAtUtc ?? now;
        }
        else if (next is CodingRunStatus.Succeeded)
        {
            run.CompletedAtUtc = task.CompletedAtUtc ?? now;
        }

        await AddEventAsync(run, previous, "task-status", task.ErrorCode, cancellationToken);
        await repository.SaveAsync(cancellationToken);
    }

    private async Task AddEventAsync(
        CodingRun run,
        CodingRunStatus? from,
        string eventType,
        string? detail,
        CancellationToken cancellationToken)
    {
        var events = await repository.ListEventsAsync(run.Id, cancellationToken);
        var sequence = events.Count == 0 ? 1 : events.Max(evt => evt.Sequence) + 1;

        await repository.AddEventAsync(
            new CodingRunEvent
            {
                Id = Guid.CreateVersion7(),
                CodingRunId = run.Id,
                Sequence = sequence,
                EventType = eventType,
                FromStatus = from?.ToString(),
                ToStatus = run.Status.ToString(),
                DetailJson = detail is { Length: > 4000 } ? detail[..4000] : detail,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            },
            cancellationToken);
    }

    private async Task<CodingRunView?> GetViewAsync(CodingRun run, CancellationToken cancellationToken)
    {
        var task = run.TaskId is { } taskId
            ? await repository.GetTaskAsync(taskId, cancellationToken)
            : null;

        var draft = await repository.GetDraftAsync(run.DraftId, cancellationToken);

        return new CodingRunView(
            run.Id,
            run.DraftId,
            draft?.Title ?? "(deleted draft)",
            run.ProjectId,
            run.AgentId,
            run.TaskId,
            run.Status.ToString(),
            task?.Status.ToString(),
            run.AttemptCount,
            run.DraftContentHash,
            run.ErrorCode,
            run.ErrorMessage,
            run.CreatedAtUtc,
            run.UpdatedAtUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.CancelRequestedAtUtc);
    }

    private static IReadOnlyList<object>? NormalizeCommands(string[]? commands)
    {
        if (commands is null || commands.Length == 0)
        {
            return [];
        }

        if (commands.Length > 10)
        {
            return null;
        }

        var normalized = new List<object>();
        foreach (var command in commands)
        {
            var trimmed = command?.Trim() ?? string.Empty;
            if (trimmed.Length is 0 or > 1000)
            {
                return null;
            }

            normalized.Add(new { command = trimmed, timeoutSeconds = 600 });
        }

        return normalized;
    }

    private static CodingRunStatus Map(TaskStatus status)
        => status switch
        {
            TaskStatus.Running => CodingRunStatus.Running,
            TaskStatus.RetryWait => CodingRunStatus.RetryWait,
            TaskStatus.Succeeded => CodingRunStatus.Succeeded,
            TaskStatus.Failed => CodingRunStatus.Failed,
            TaskStatus.Cancelled => CodingRunStatus.Canceled,
            _ => CodingRunStatus.Queued
        };

    private static bool IsTerminal(CodingRunStatus status)
        => status is CodingRunStatus.Succeeded or CodingRunStatus.Failed or CodingRunStatus.Canceled;

    private static string Hash(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
