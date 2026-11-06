using AgentOS.Runner;

namespace AgentOS.Api.Tests;

public sealed class ValidationCommandRunnerTests
{
    private readonly ValidationCommandRunner _runner = new();
    private readonly List<string> _logs = [];

    [Fact]
    public async Task Runs_a_command_and_captures_output()
    {
        var result = await _runner.RunAsync(
            Path.GetTempPath(),
            new ValidationCommand("echo hello", 10),
            (level, message) => _logs.Add($"{level}:{message}"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Output);
        Assert.Contains(_logs, line => line.Contains("hello"));
    }

    [Fact]
    public async Task Non_zero_exit_is_a_validation_failure()
    {
        var command = OperatingSystem.IsWindows() ? "exit /b 3" : "exit 3";

        var result = await _runner.RunAsync(
            Path.GetTempPath(),
            new ValidationCommand(command, 10),
            (_, _) => { },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("validation-failed", result.ErrorCode);
    }

    [Fact]
    public async Task Runner_secrets_are_not_inherited_by_validation_commands()
    {
        const string secret = "agentos-validation-secret";
        var variable = "AGENTOS_VALIDATION_SECRET";
        Environment.SetEnvironmentVariable(variable, secret);

        var command = OperatingSystem.IsWindows()
            ? $"echo %{variable}%"
            : $"echo ${variable}";

        try
        {
            var result = await _runner.RunAsync(
                Path.GetTempPath(),
                new ValidationCommand(command, 10),
                (_, _) => { },
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.DoesNotContain(secret, result.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task Timeout_kills_the_command()
    {
        var command = OperatingSystem.IsWindows()
            ? "ping -n 10 127.0.0.1 > nul"
            : "sleep 10";

        var result = await _runner.RunAsync(
            Path.GetTempPath(),
            new ValidationCommand(command, 1),
            (_, _) => { },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("validation-timeout", result.ErrorCode);
    }
}
