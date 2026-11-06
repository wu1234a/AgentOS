using AgentOS.AgentAdapters.Processes;

namespace AgentOS.Api.Tests;

public sealed class LineBufferTests
{
    [Fact]
    public void A_line_split_across_reads_is_emitted_once()
    {
        var buffer = new LineBuffer();

        foreach (var character in "sk-abc")
        {
            buffer.Append(character);
        }

        Assert.True(buffer.HasPartialLine);

        foreach (var character in "defghijk\n")
        {
            if (character == '\n')
            {
                Assert.Equal("sk-abcdefghijk", buffer.TakeCompletedLine());
            }
            else
            {
                buffer.Append(character);
            }
        }

        Assert.False(buffer.HasPartialLine);
    }
}

public sealed class ExternalProcessRunnerBlockedInputTests
{
    private readonly ExternalProcessRunner _runner = new(TimeProvider.System);

    [Fact]
    public async Task Blocked_stdin_write_is_bounded_by_the_timeout()
    {
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "ping -n 30 127.0.0.1 >nul" })
            : ("bash", new[] { "-c", "sleep 30" });

        var request = new ProcessRunRequest(
            fileName,
            arguments,
            Path.GetTempPath(),
            new string('x', 2_000_000),
            TimeSpan.FromMilliseconds(700),
            TestEnvironment(),
            200_000,
            Path.Combine(Path.GetTempPath(), "agentos-tests", Guid.NewGuid().ToString("N")),
            []);

        var result = await _runner.RunAsync(request, CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.True(result.FinishedAtUtc - result.StartedAtUtc < TimeSpan.FromSeconds(15));
    }

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
}
