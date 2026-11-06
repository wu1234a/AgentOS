using AgentOS.AgentAdapters;
using AgentOS.AgentAdapters.Codex;
using AgentOS.AgentAdapters.Processes;
using AgentOS.Runner;
using AgentOS.Runner.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class CodexTaskHandlerTests
{
    private readonly FakeAgentAdapter _adapter = new();
    private readonly List<string> _logs = [];

    [Fact]
    public async Task Success_maps_to_succeeded_and_logs_the_summary()
    {
        _adapter.Result = Result(AgentExecutionStatus.Succeeded, 0, summary: "all good");

        var result = await CreateHandler().ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(HandlerOutcome.Succeeded, result.Outcome);
        Assert.Equal("all good", result.Summary);
        Assert.Contains(_logs, line => line.Contains("all good"));
        Assert.Contains("Adapter test", _adapter.LastRequest!.Prompt);
        Assert.Equal(AgentSandboxMode.ReadOnly, _adapter.LastRequest.Policy.SandboxMode);
    }

    [Fact]
    public async Task Timeout_is_a_retryable_failure()
    {
        _adapter.Result = Result(AgentExecutionStatus.TimedOut, null, errorCode: "codex-timeout");

        var result = await CreateHandler().ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(HandlerOutcome.Failed, result.Outcome);
        Assert.True(result.Retryable);
        Assert.Equal("codex-timeout", result.ErrorCode);
    }

    [Fact]
    public async Task Unavailable_cli_is_not_retryable()
    {
        _adapter.Result = Result(AgentExecutionStatus.Unavailable, null, errorCode: "codex-not-found");

        var result = await CreateHandler().ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(HandlerOutcome.Failed, result.Outcome);
        Assert.False(result.Retryable);
        Assert.Equal("codex-not-found", result.ErrorCode);
    }

    [Fact]
    public async Task Model_profile_is_forwarded_to_the_adapter()
    {
        _adapter.Result = Result(AgentExecutionStatus.Succeeded, 0);
        var profile = new AgentOS.Application.ModelProfiles.ResolvedModelProfile(
            Guid.NewGuid(),
            "codex-default",
            "codex-cli",
            "profile-model",
            "cli",
            "{}",
            "OPENAI_API_KEY",
            120,
            "agent");

        var result = await CreateHandler().ExecuteAsync(Context(profile), CancellationToken.None);

        Assert.Equal(HandlerOutcome.Succeeded, result.Outcome);
        Assert.Equal("profile-model", _adapter.LastRequest!.Model);
        Assert.Equal(120, _adapter.LastRequest.TimeoutSeconds);
    }

    [Fact]
    public async Task Unsupported_provider_is_not_retryable()
    {
        _adapter.Result = Result(AgentExecutionStatus.Succeeded, 0);
        var profile = new AgentOS.Application.ModelProfiles.ResolvedModelProfile(
            Guid.NewGuid(),
            "kimi-planner",
            "openai",
            "kimi-k2",
            "api",
            "{}",
            "KIMI_API_KEY",
            120,
            "agent");

        var result = await CreateHandler().ExecuteAsync(Context(profile), CancellationToken.None);

        Assert.Equal(HandlerOutcome.Failed, result.Outcome);
        Assert.False(result.Retryable);
        Assert.Equal("model-provider-unsupported", result.ErrorCode);
        Assert.Null(_adapter.LastRequest);
    }

    [Fact]
    public async Task Cancellation_maps_to_cancelled()
    {
        _adapter.Result = Result(AgentExecutionStatus.Cancelled, null);

        var result = await CreateHandler().ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(HandlerOutcome.Cancelled, result.Outcome);
    }

    private CodexTaskHandler CreateHandler() => new(
        _adapter,
        Options.Create(new CodexCliOptions { SandboxMode = "read-only" }),
        NullLogger<CodexTaskHandler>.Instance);

    private TaskExecutionContext Context(AgentOS.Application.ModelProfiles.ResolvedModelProfile? profile = null) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "TASK-CODEX",
        "Adapter test",
        "normal",
        "demo",
        "coder",
        "Coder",
        null,
        null,
        Path.GetTempPath(),
        "{}",
        1,
        600,
        (_, message) => _logs.Add(message),
        profile);

    private static AgentExecutionResult Result(
        AgentExecutionStatus status,
        int? exitCode,
        string? summary = null,
        string? errorCode = null)
        => new(
            status,
            exitCode,
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            null,
            summary,
            errorCode,
            errorCode,
            null);
}

public sealed class FakeAgentAdapter : IAgentAdapter
{
    public string Code => "codex-cli";

    public AgentExecutionResult Result { get; set; } = new(
        AgentExecutionStatus.Succeeded,
        0,
        DateTime.UtcNow,
        DateTime.UtcNow,
        null,
        null,
        null,
        null,
        null,
        null);

    public AgentExecutionRequest? LastRequest { get; private set; }

    public Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(Result);
    }
}
