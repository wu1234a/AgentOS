using AgentOS.Application.Tasks;
using AgentOS.Runner.Handlers;
using Microsoft.Extensions.Options;

namespace AgentOS.Runner;

/// <summary>
/// Executes one claimed task: workspace policy, start, handler, heartbeat/cancellation, log
/// upload and exactly one terminal report. A failed terminal report is never turned into a
/// success; the lease simply expires and the control plane recovers the task.
/// </summary>
public sealed class TaskProcessor(
    IRunnerApi api,
    WorkspacePolicy workspace,
    IWorktreeManager worktrees,
    ITaskHandler handler,
    IOptions<RunnerOptions> options,
    TimeProvider timeProvider,
    ILogger<TaskProcessor> logger)
{
    public async Task ProcessAsync(ClaimedTask task, CancellationToken stoppingToken)
    {
        var decision = workspace.Validate(task.ProjectSlug);
        if (!decision.Allowed)
        {
            logger.LogWarning(
                "Rejected task {TaskId}: {Reason}.",
                task.TaskId,
                decision.Reason);
            await FailBestEffortAsync(
                task,
                "workspace-rejected",
                decision.Reason ?? "workspace rejected",
                retryable: false,
                cancelled: false,
                stoppingToken);
            return;
        }

        WorktreeContext? worktree = null;
        var workspacePath = decision.WorkspacePath!;
        WorktreeCreateResult? created = null;

        if (worktrees.TryParseRequest(task.InputJson, out var worktreeRequest, out var requestError))
        {
            if (worktreeRequest is null)
            {
                await FailBestEffortAsync(
                    task,
                    "worktree-invalid",
                    requestError ?? "Worktree request is invalid.",
                    retryable: false,
                    cancelled: false,
                    stoppingToken);
                return;
            }

            created = await worktrees.CreateAsync(workspacePath, worktreeRequest, stoppingToken);
        }
        else if (!string.IsNullOrWhiteSpace(options.Value.WorktreeRoot))
        {
            // A configured worktree root makes isolation the default. The task can still opt into
            // an explicit base revision with the git-worktree request above.
            created = await worktrees.CreateAutomaticAsync(workspacePath, task.TaskId, stoppingToken);
        }

        if (created is not null)
        {
            if (!created.Success || created.Context is null)
            {
                logger.LogWarning(
                    "Worktree creation failed for task {TaskId}: {Code}.",
                    task.TaskId,
                    created.ErrorCode);
                await FailBestEffortAsync(
                    task,
                    created.ErrorCode ?? "worktree-create-failed",
                    created.ErrorMessage ?? "Worktree creation failed.",
                    retryable: false,
                    cancelled: false,
                    stoppingToken);
                return;
            }

            worktree = created.Context;
            workspacePath = created.Context.Path;
        }

        try
        {
            await api.StartAsync(task.TaskId, task.Lease.AttemptId, task.Lease.Token, null, stoppingToken);
        }
        catch (RunnerApiException exception) when (!exception.IsTransient)
        {
            logger.LogWarning(
                "Could not start task {TaskId}: {Code} ({Status}). The lease may have expired.",
                task.TaskId,
                exception.Code,
                exception.StatusCode);
            if (worktree is not null)
            {
                await worktrees.RemoveAsync(worktree, CancellationToken.None);
            }

            return;
        }
        catch
        {
            if (worktree is not null)
            {
                await worktrees.RemoveAsync(worktree, CancellationToken.None);
            }

            throw;
        }

        var logs = new LogBuffer(api, task, Math.Max(1, options.Value.MaxLogEntriesPerBatch), timeProvider, logger);

        using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var interruption = new InterruptionState();
        var heartbeatTask = RunHeartbeatAsync(task, logs, taskCts, interruption, logger);

        try
        {
            var context = new TaskExecutionContext(
                task.TaskId,
                task.Lease.AttemptId,
                task.TaskCode,
                task.Title,
                task.Priority,
                task.ProjectSlug,
                task.AgentCode,
                task.AgentRole,
                task.SkillCode,
                task.SkillContent,
                workspacePath,
                task.InputJson,
                task.Lease.AttemptNumber,
                task.TimeoutSeconds,
                logs.Add,
                task.ModelProfile);

            var result = await handler.ExecuteAsync(context, taskCts.Token);

            await UploadArtifactsAsync(task, result.Artifacts, logger);
            await logs.FlushAsync(stoppingToken);

            switch (result.Outcome)
            {
                case HandlerOutcome.Succeeded:
                    await CompleteAsync(task, result.ExitCode, stoppingToken);
                    break;

                case HandlerOutcome.Cancelled:
                    await ReportInterruptionAsync(task, interruption, stoppingToken, logs, logger);
                    break;

                default:
                    await FailBestEffortAsync(
                        task,
                        result.ErrorCode ?? "handler-failed",
                        result.ErrorMessage ?? "Task handler reported failure.",
                        result.Retryable,
                        cancelled: false,
                        stoppingToken);
                    break;
            }
        }
        catch (OperationCanceledException) when (taskCts.IsCancellationRequested)
        {
            await ReportInterruptionAsync(task, interruption, stoppingToken, logs, logger);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Handler {Handler} crashed on task {TaskId}.", handler.Name, task.TaskId);
            await logs.FlushAsync(CancellationToken.None);
            await FailBestEffortAsync(
                task,
                "handler-error",
                exception.Message,
                retryable: true,
                cancelled: false,
                CancellationToken.None);
        }
        finally
        {
            taskCts.Cancel();

            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when the task finished first.
            }

            if (worktree is not null)
            {
                await worktrees.RemoveAsync(worktree, CancellationToken.None);
            }
        }
    }

    private async Task RunHeartbeatAsync(
        ClaimedTask task,
        LogBuffer logs,
        CancellationTokenSource taskCts,
        InterruptionState interruption,
        ILogger<TaskProcessor> runLogger)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.HeartbeatIntervalSeconds));
        var consecutiveFailures = 0;

        while (!taskCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, taskCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await logs.FlushAsync(CancellationToken.None);

            try
            {
                var response = await api.HeartbeatTaskAsync(
                    task.TaskId,
                    task.Lease.AttemptId,
                    task.Lease.Token,
                    taskCts.Token);

                consecutiveFailures = 0;

                if (response.CancelRequested)
                {
                    runLogger.LogInformation("Cancellation requested for task {TaskId}.", task.TaskId);
                    interruption.Set(InterruptionReason.Requested);
                    taskCts.Cancel();
                    return;
                }
            }
            catch (RunnerApiException exception) when (!exception.IsTransient)
            {
                // 403/404 means the lease is gone; continuing would be wasted work.
                runLogger.LogWarning(
                    "Lease lost for task {TaskId}: {Code}.",
                    task.TaskId,
                    exception.Code);
                interruption.Set(InterruptionReason.LeaseLost);
                taskCts.Cancel();
                return;
            }
            catch (Exception exception)
            {
                consecutiveFailures++;
                runLogger.LogWarning(
                    exception,
                    "Heartbeat failed for task {TaskId} ({Failures}/3).",
                    task.TaskId,
                    consecutiveFailures);

                if (consecutiveFailures >= 3)
                {
                    runLogger.LogError(
                        "Stopping task {TaskId}: heartbeat failed three times in a row.",
                        task.TaskId);
                    interruption.Set(InterruptionReason.HeartbeatFailure);
                    taskCts.Cancel();
                    return;
                }
            }
        }
    }

    /// <summary>Maps the reason the task was interrupted to the correct terminal report.</summary>
    private async Task ReportInterruptionAsync(
        ClaimedTask task,
        InterruptionState interruption,
        CancellationToken stoppingToken,
        LogBuffer logs,
        ILogger<TaskProcessor> runLogger)
    {
        await logs.FlushAsync(CancellationToken.None);

        switch (interruption.Value)
        {
            case InterruptionReason.LeaseLost:
                // The lease is gone, so any report would be rejected; let recovery handle it.
                runLogger.LogWarning("Not reporting task {TaskId}: the lease was already lost.", task.TaskId);
                return;

            case InterruptionReason.HeartbeatFailure:
                await FailBestEffortAsync(
                    task,
                    "heartbeat-lost",
                    "Runner could not renew the lease; the task will be retried.",
                    retryable: true,
                    cancelled: false,
                    CancellationToken.None);
                return;

            default:
                var shuttingDown = stoppingToken.IsCancellationRequested;
                await FailBestEffortAsync(
                    task,
                    shuttingDown ? "runner-shutdown" : "cancelled",
                    shuttingDown ? "Runner is shutting down." : "Cancelled by AgentOS.",
                    retryable: shuttingDown,
                    cancelled: !shuttingDown,
                    CancellationToken.None);
                return;
        }
    }

    private async Task UploadArtifactsAsync(
        ClaimedTask task,
        IReadOnlyList<TaskArtifactFile>? artifacts,
        ILogger<TaskProcessor> runLogger)
    {
        if (artifacts is null || artifacts.Count == 0)
        {
            return;
        }

        foreach (var artifact in artifacts)
        {
            try
            {
                await api.UploadArtifactAsync(
                    task.TaskId,
                    task.Lease.AttemptId,
                    task.Lease.Token,
                    artifact.Name,
                    artifact.MediaType,
                    artifact.Path,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                // Artifact upload is best effort; it must not change the task outcome.
                runLogger.LogWarning(
                    exception,
                    "Could not upload artifact {Name} for task {TaskId}.",
                    artifact.Name,
                    task.TaskId);
            }
        }
    }

    private async Task CompleteAsync(ClaimedTask task, int exitCode, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await api.CompleteAsync(
                    task.TaskId,
                    task.Lease.AttemptId,
                    task.Lease.Token,
                    exitCode,
                    cancellationToken);
                return;
            }
            catch (RunnerApiException exception) when (exception.IsTransient && attempt < 3)
            {
                logger.LogWarning(
                    "Complete failed for task {TaskId} ({Code}); retrying.",
                    task.TaskId,
                    exception.Code);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
            catch (RunnerApiException exception)
            {
                // Never claim success locally; the lease expires and the control plane recovers.
                logger.LogError(
                    "Could not report completion for task {TaskId}: {Code} ({Status}).",
                    task.TaskId,
                    exception.Code,
                    exception.StatusCode);
                return;
            }
        }
    }

    private async Task FailBestEffortAsync(
        ClaimedTask task,
        string errorCode,
        string errorMessage,
        bool retryable,
        bool cancelled,
        CancellationToken cancellationToken)
    {
        try
        {
            await api.FailAsync(
                task.TaskId,
                task.Lease.AttemptId,
                task.Lease.Token,
                errorCode,
                errorMessage,
                retryable,
                cancelled,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not report failure for task {TaskId}; the lease will expire.",
                task.TaskId);
        }
    }

    /// <summary>Bounded, best-effort log uploader; control-plane failures never fail the task.</summary>
    private enum InterruptionReason
    {
        None = 0,
        Requested = 1,
        HeartbeatFailure = 2,
        LeaseLost = 3
    }

    private sealed class InterruptionState
    {
        private int _value;

        public InterruptionReason Value => (InterruptionReason)Volatile.Read(ref _value);

        public void Set(InterruptionReason reason) => Volatile.Write(ref _value, (int)reason);
    }

    private sealed class LogBuffer(
        IRunnerApi api,
        ClaimedTask task,
        int batchSize,
        TimeProvider timeProvider,
        ILogger logger)
    {
        private readonly object _gate = new();
        private readonly List<TaskLogEntry> _entries = [];
        private readonly List<Task> _uploads = [];
        private int _sequence;

        public void Add(string level, string message)
        {
            TaskLogEntry[]? batch = null;

            lock (_gate)
            {
                _entries.Add(new TaskLogEntry(++_sequence, level, message, timeProvider.GetUtcNow().UtcDateTime));

                if (_entries.Count >= batchSize)
                {
                    batch = [.. _entries];
                    _entries.Clear();
                }
            }

            if (batch is not null)
            {
                StartUpload(batch);
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            Task[] pending;

            lock (_gate)
            {
                pending = [.. _uploads];
                _uploads.Clear();
            }

            // A filled batch may still be in flight; drain it before the terminal report so its
            // lines are stored while the lease is still valid.
            await Task.WhenAll(pending);

            TaskLogEntry[] batch;

            lock (_gate)
            {
                if (_entries.Count == 0)
                {
                    return;
                }

                batch = [.. _entries];
                _entries.Clear();
            }

            await SendAsync(batch, cancellationToken);
        }

        private void StartUpload(TaskLogEntry[] batch)
        {
            var upload = SendAsync(batch, CancellationToken.None);

            lock (_gate)
            {
                _uploads.Add(upload);
            }
        }

        private async Task SendAsync(TaskLogEntry[] batch, CancellationToken cancellationToken)
        {
            try
            {
                await api.AppendLogsAsync(
                    task.TaskId,
                    task.Lease.AttemptId,
                    task.Lease.Token,
                    batch,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Log upload failed for task {TaskId}; dropping {Count} line(s).", task.TaskId, batch.Length);
            }
        }
    }
}
