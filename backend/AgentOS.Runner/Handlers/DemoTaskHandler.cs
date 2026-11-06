namespace AgentOS.Runner.Handlers;

/// <summary>
/// Side-effect-free handler used until AOS-007 adds the Codex adapter. It proves the full
/// lifecycle (start, log, heartbeat, complete) without touching the workspace.
/// </summary>
public sealed class DemoTaskHandler(ILogger<DemoTaskHandler> logger) : ITaskHandler
{
    private const int Steps = 3;

    public string Name => "demo";

    public async Task<TaskHandlerResult> ExecuteAsync(
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        context.Log("info", $"demo handler started for {context.TaskCode} ({context.ProjectSlug})");
        logger.LogInformation("Demo handler started for task {TaskId}.", context.TaskId);

        try
        {
            for (var step = 1; step <= Steps; step++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                context.Log("info", $"demo step {step}/{Steps}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Log("warn", "demo handler cancelled");
            return TaskHandlerResult.Cancelled();
        }

        context.Log("info", "demo handler finished");
        return TaskHandlerResult.Succeeded();
    }
}
