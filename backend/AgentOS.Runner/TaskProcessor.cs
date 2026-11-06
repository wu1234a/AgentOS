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
            return;
        }

        var logs = new LogBuffer(api, task, Math.Max(1, options.Value.MaxLogEntriesPerBatch), timeProvider, logger);

        using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeatTask = RunHeartbeatAsync(task, logs, taskCts, logger);

        try
        {
            var context = new TaskExecutionContext(
                task.TaskId,
                task.TaskCode,
                task.ProjectSlug,
                decision.WorkspacePath!,
                task.InputJson,
                task.Lease.AttemptNumber,
                logs.Add);

            var result = await handler.ExecuteAsync(context, taskCts.Token);

            await logs.FlushAsync(stoppingToken);

            switch (result.Outcome)
            {
                case HandlerOutcome.Succeeded:
                    await CompleteAsync(task, result.ExitCode, stoppingToken);
                    break;

                case HandlerOutcome.Cancelled:
                    var shuttingDown = stoppingToken.IsCancellationRequested;
                    await FailBestEffortAsync(
                        task,
                        shuttingDown ? "runner-shutdown" : "cancelled",
                        shuttingDown ? "Runner is shutting down." : "Cancelled by AgentOS.",
                        retryable: shuttingDown,
                        cancelled: !shuttingDown,
                        stoppingToken);
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
            await logs.FlushAsync(CancellationToken.None);

            var shuttingDown = stoppingToken.IsCancellationRequested;
            await FailBestEffortAsync(
                task,
                shuttingDown ? "runner-shutdown" : "cancelled",
                shuttingDown ? "Runner is shutting down." : "Cancelled by AgentOS.",
                retryable: shuttingDown,
                cancelled: !shuttingDown,
                CancellationToken.None);
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
        }
    }

    private async Task RunHeartbeatAsync(
        ClaimedTask task,
        LogBuffer logs,
        CancellationTokenSource taskCts,
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
                    taskCts.Cancel();
                    return;
                }
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
    private sealed class LogBuffer(
        IRunnerApi api,
        ClaimedTask task,
        int batchSize,
        TimeProvider timeProvider,
        ILogger logger)
    {
        private readonly object _gate = new();
        private readonly List<TaskLogEntry> _entries = [];
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
                _ = SendAsync(batch, CancellationToken.None);
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
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
