using System.Diagnostics;
using System.Text;

namespace AgentOS.Runner;

/// <summary>
/// Runs an approved validation command in the task workspace with a minimal environment, a
/// per-command timeout and bounded output. Secrets from the Runner environment are never passed.
/// </summary>
public sealed class ValidationCommandRunner : IValidationCommandRunner
{
    private const int MaxOutputChars = 4000;
    private const int MaxLoggedLines = 50;

    private static readonly string[] AllowedEnvironmentVariables = OperatingSystem.IsWindows()
        ? ["PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "COMSPEC", "PATHEXT", "HOME",
           "LOCALAPPDATA", "APPDATA", "PROGRAMDATA", "ALLUSERSPROFILE", "HOMEDRIVE", "HOMEPATH",
           "DOTNET_ROOT", "NUGET_PACKAGES", "SystemDrive", "ProgramFiles", "ProgramFiles(x86)",
           "ProgramW6432", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432",
           "USERNAME", "USERDOMAIN", "USERDOMAIN_ROAMINGPROFILE", "NUMBER_OF_PROCESSORS",
           "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "PSModulePath", "PUBLIC", "SESSIONNAME"]
        : ["PATH", "HOME", "TMPDIR", "LANG", "LC_ALL"];

    public async Task<ValidationCommandResult> RunAsync(
        string workspacePath,
        ValidationCommand command,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            // cmd.exe does not understand the C-runtime backslash escaping produced by
            // ArgumentList; pass a cmd-compatible quoted command string instead.
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/d /s /c \"{command.Command}\"";
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command.Command);
        }

        startInfo.Environment.Clear();
        foreach (var name in AllowedEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stopwatch = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(command.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            if (!process.Start())
            {
                return Failure("validation-error", "Validation process did not start.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Failure("validation-error", $"Validation process could not start: {exception.Message}");
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = DrainAsync(process.StandardOutput, stdout, linked.Token);
        var stderrTask = DrainAsync(process.StandardError, stderr, linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            TryKill(process);
            return Failure("validation-timeout", $"Validation command exceeded {command.TimeoutSeconds}s.") with { DurationMs = (int)stopwatch.ElapsedMilliseconds, Command = command.Command };
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        stopwatch.Stop();
        var output = Combine(stdout.ToString(), stderr.ToString());
        var exitCode = process.ExitCode;

        if (output.Length > 0)
        {
            var lines = output.Split('\n');
            foreach (var line in lines.Take(MaxLoggedLines))
            {
                log("info", $"validation: {line.TrimEnd()}");
            }

            if (lines.Length > MaxLoggedLines)
            {
                log("info", $"validation: ... {lines.Length - MaxLoggedLines} more line(s)");
            }
        }

        if (exitCode == 0)
        {
            return new ValidationCommandResult(true, 0, output, null, null, (int)stopwatch.ElapsedMilliseconds, command.Command);
        }

        return new ValidationCommandResult(
            false,
            exitCode,
            output,
            "validation-failed",
            $"Validation command failed with exit code {exitCode}.",
            (int)stopwatch.ElapsedMilliseconds,
            command.Command);
    }

    private static async Task DrainAsync(StreamReader reader, StringBuilder sink, CancellationToken cancellationToken)
    {
        var buffer = new char[1024];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return;
            }

            lock (sink)
            {
                var remaining = MaxOutputChars - sink.Length;
                if (remaining > 0)
                {
                    sink.Append(buffer, 0, Math.Min(read, remaining));
                }
            }
        }
    }

    private static string Combine(string stdout, string stderr)
    {
        var builder = new StringBuilder(stdout);
        if (stderr.Length > 0)
        {
            builder.AppendLine();
            builder.Append(stderr);
        }

        return builder.Length <= MaxOutputChars ? builder.ToString() : builder.ToString(0, MaxOutputChars);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process exited between the check and the kill.
        }
    }

    private static ValidationCommandResult Failure(string code, string message)
        => new(false, null, string.Empty, code, message);
}
