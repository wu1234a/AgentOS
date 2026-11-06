using AgentOS.AgentAdapters.Processes;

namespace AgentOS.Api.Tests;

/// <summary>Scriptable process runner used by adapter tests.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public ProcessRunRequest? LastRequest { get; private set; }

    public ProcessRunResult Result { get; set; } = new(
        0,
        false,
        false,
        string.Empty,
        string.Empty,
        null,
        null,
        DateTime.UtcNow,
        DateTime.UtcNow);

    public Exception? Throw { get; set; }

    public Func<ProcessRunRequest, ProcessRunResult>? Handler { get; set; }

    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;

        if (Throw is not null)
        {
            return Task.FromException<ProcessRunResult>(Throw);
        }

        return Task.FromResult(Handler?.Invoke(request) ?? Result);
    }
}

/// <summary>Real process runner tests; all commands are cross-platform-safe.</summary>
public sealed class ExternalProcessRunnerTests
{
    private readonly ExternalProcessRunner _runner = new(TimeProvider.System);

    [Fact]
    public async Task Captures_stdout_and_exit_code()
    {
        var (fileName, arguments) = Echo("process-ok");

        var result = await _runner.RunAsync(Request(fileName, arguments, timeoutSeconds: 60), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("process-ok", result.Stdout);
        Assert.NotNull(result.StdoutPath);
        Assert.True(File.Exists(result.StdoutPath));
    }

    [Fact]
    public async Task Non_zero_exit_is_captured()
    {
        var (fileName, arguments) = ExitWith(3);

        var result = await _runner.RunAsync(Request(fileName, arguments, timeoutSeconds: 60), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Missing_executable_throws_process_start_exception()
    {
        await Assert.ThrowsAsync<ProcessStartException>(() => _runner.RunAsync(
            Request(Path.Combine(Path.GetTempPath(), "no-such-binary-" + Guid.NewGuid().ToString("N")), [], 5),
            CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_kills_the_process_tree()
    {
        var result = await _runner.RunAsync(
            SleepRequest(TimeSpan.FromMilliseconds(500)),
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.True(result.FinishedAtUtc - result.StartedAtUtc < TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Cancellation_kills_the_process_tree()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var result = await _runner.RunAsync(SleepRequest(TimeSpan.FromMilliseconds(500)), cancellation.Token);

        Assert.True(result.Cancelled);
        Assert.False(result.TimedOut);
    }

    private static ProcessRunRequest Request(string fileName, string[] arguments, int timeoutSeconds)
        => new(
            fileName,
            arguments,
            Path.GetTempPath(),
            null,
            TimeSpan.FromSeconds(timeoutSeconds),
            TestEnvironment(),
            200_000,
            Path.Combine(Path.GetTempPath(), "agentos-tests", Guid.NewGuid().ToString("N")),
            []);

    private static (string FileName, string[] Arguments) Echo(string text)
        => OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", $"echo {text}"])
            : ("bash", ["-c", $"echo {text}"]);

    private static (string FileName, string[] Arguments) ExitWith(int code)
        => OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", $"exit {code}"])
            : ("bash", ["-c", $"exit {code}"]);

    private static Dictionary<string, string> TestEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "PATH", "PATHEXT", "SYSTEMROOT", "WINDIR", "COMSPEC", "TEMP", "TMP", "HOME", "USERPROFILE" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    private static ProcessRunRequest SleepRequest(TimeSpan timeout)
    {
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "ping -n 30 127.0.0.1 >nul" })
            : ("bash", new[] { "-c", "sleep 30" });

        return new ProcessRunRequest(
            fileName,
            arguments,
            Path.GetTempPath(),
            null,
            timeout,
            TestEnvironment(),
            200_000,
            Path.Combine(Path.GetTempPath(), "agentos-tests", Guid.NewGuid().ToString("N")),
            []);
    }
}
