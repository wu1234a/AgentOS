using AgentOS.Application.Tasks;
using AgentOS.Api.Tests.Fakes;
using AgentOS.Runner;
using AgentOS.Runner.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class TaskProcessorTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeRunnerApi _api = new();
    private readonly FakeTaskHandler _handler = new();
    private readonly string _root;
    private readonly string _workspace;

    public TaskProcessorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentos-runner-tests", Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "demo");
        Directory.CreateDirectory(_workspace);
    }

    [Fact]
    public async Task Successful_handler_starts_logs_and_completes()
    {
        var task = NewTask();

        await CreateProcessor().ProcessAsync(task, CancellationToken.None);

        Assert.Contains(task.TaskId, _api.Started);
        Assert.Contains(task.TaskId, _api.Completed);
        Assert.Empty(_api.Failed);
        Assert.NotEmpty(_api.LogBatches);
        Assert.Contains(_api.LogBatches.SelectMany(batch => batch), entry => entry.Message == "fake handler started");
    }

    [Fact]
    public async Task Handler_failure_is_reported_as_retryable_failure()
    {
        _handler.Fail = true;
        var task = NewTask();

        await CreateProcessor().ProcessAsync(task, CancellationToken.None);

        Assert.Empty(_api.Completed);
        var failure = Assert.Single(_api.Failed);
        Assert.Equal("fake-error", failure.Code);
        Assert.True(failure.Retryable);
        Assert.False(failure.Cancelled);
    }

    [Fact]
    public async Task Workspace_rejection_never_starts_the_handler()
    {
        var task = NewTask(projectSlug: "not-configured");

        await CreateProcessor().ProcessAsync(task, CancellationToken.None);

        Assert.Empty(_api.Started);
        Assert.Empty(_api.Completed);
        var failure = Assert.Single(_api.Failed);
        Assert.Equal("workspace-rejected", failure.Code);
        Assert.False(failure.Retryable);
    }

    [Fact]
    public async Task Heartbeat_cancellation_stops_the_handler_and_reports_cancelled()
    {
        _api.CancelOnHeartbeat = true;
        _handler.CancelUntilCancelled = true;
        var task = NewTask();

        await CreateProcessor(heartbeatSeconds: 1).ProcessAsync(task, CancellationToken.None);

        Assert.Empty(_api.Completed);
        var failure = Assert.Single(_api.Failed);
        Assert.Equal("cancelled", failure.Code);
        Assert.True(failure.Cancelled);
        Assert.False(failure.Retryable);
    }

    [Fact]
    public async Task Host_shutdown_requeues_the_task()
    {
        _handler.CancelUntilCancelled = true;
        var task = NewTask();
        using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await CreateProcessor().ProcessAsync(task, stopping.Token);

        Assert.Empty(_api.Completed);
        var failure = Assert.Single(_api.Failed);
        Assert.Equal("runner-shutdown", failure.Code);
        Assert.True(failure.Retryable);
        Assert.False(failure.Cancelled);
    }

    [Fact]
    public async Task Completion_rejected_by_the_api_is_never_reported_as_success()
    {
        _api.CompleteException = new RunnerApiException(403, "invalid-lease-token", "rejected");

        await CreateProcessor().ProcessAsync(NewTask(), CancellationToken.None);

        Assert.Empty(_api.Completed);
        Assert.Empty(_api.Failed);
    }

    [Fact]
    public async Task Heartbeat_failure_is_reported_as_retryable_not_cancelled()
    {
        _api.HeartbeatFailuresRemaining = 3;
        _handler.CancelUntilCancelled = true;
        var task = NewTask();

        await CreateProcessor(heartbeatSeconds: 1).ProcessAsync(task, CancellationToken.None);

        Assert.Empty(_api.Completed);
        var failure = Assert.Single(_api.Failed);
        Assert.Equal("heartbeat-lost", failure.Code);
        Assert.True(failure.Retryable);
        Assert.False(failure.Cancelled);
    }

    [Fact]
    public async Task Filled_log_batches_are_drained_before_completion()
    {
        _api.LogUploadDelay = TimeSpan.FromMilliseconds(250);

        await CreateProcessor(logBatchSize: 1).ProcessAsync(NewTask(), CancellationToken.None);

        Assert.NotEmpty(_api.LogBatches);
        Assert.Contains(_api.LogBatches.SelectMany(batch => batch), entry => entry.Message == "fake handler started");
    }

    private TaskProcessor CreateProcessor(int heartbeatSeconds = 20, int logBatchSize = 2) => new(
        _api,
        new WorkspacePolicy(Options.Create(new RunnerOptions
        {
            WorkspaceRoots = [_root],
            Projects = [new ProjectWorkspace { Slug = "demo", Path = _workspace }],
            HeartbeatIntervalSeconds = heartbeatSeconds
        })),
        _handler,
        Options.Create(new RunnerOptions
        {
            HeartbeatIntervalSeconds = heartbeatSeconds,
            MaxLogEntriesPerBatch = logBatchSize
        }),
        new ManualTimeProvider(Now),
        NullLogger<TaskProcessor>.Instance);

    private ClaimedTask NewTask(string projectSlug = "demo") => new(
        Guid.NewGuid(),
        projectSlug,
        "TASK-TEST",
        "Runner test",
        "normal",
        "{}",
        null,
        null,
        Guid.NewGuid(),
        null,
        null,
        2,
        1800,
        new TaskLease(Guid.NewGuid(), 1, "lease-token", Now.AddMinutes(5)));
}
