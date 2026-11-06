using System.Diagnostics;
using System.Text;
using AgentOS.AgentAdapters;

namespace AgentOS.AgentAdapters.Processes;

/// <summary>Security-relevant process start failure (for example the executable is missing).</summary>
public sealed class ProcessStartException : Exception
{
    public ProcessStartException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string? StandardInput,
    TimeSpan Timeout,
    IReadOnlyDictionary<string, string> Environment,
    int MaxOutputBytes,
    string? ArtifactDirectory = null,
    IReadOnlyList<string>? Redactions = null,
    Action<string, string>? OnOutput = null);

public sealed record ProcessRunResult(
    int ExitCode,
    bool TimedOut,
    bool Cancelled,
    string Stdout,
    string Stderr,
    string? StdoutPath,
    string? StderrPath,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc);

/// <summary>Executes an external process with a fixed argument list and captured streams.</summary>
public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Real process runner. Arguments are passed via <see cref="ProcessStartInfo.ArgumentList"/> only,
/// stdout/stderr are drained concurrently, the output is capped and redacted, and both timeout
/// and cancellation kill the entire process tree.
/// </summary>
public sealed class ExternalProcessRunner(TimeProvider timeProvider) : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var pair in request.Environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        var artifactDirectory = request.ArtifactDirectory
            ?? Path.Combine(Path.GetTempPath(), "agentos", "process", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactDirectory);

        var startedAt = timeProvider.GetUtcNow().UtcDateTime;
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                throw new ProcessStartException($"Could not start '{request.FileName}'.");
            }
        }
        catch (Exception exception) when (exception is not ProcessStartException)
        {
            throw new ProcessStartException(
                $"Could not start '{request.FileName}': {exception.Message}",
                exception);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var stdoutTask = PumpAsync(
            process.StandardOutput,
            stdout,
            request,
            "stdout",
            "info",
            request.MaxOutputBytes,
            request.OnOutput);
        var stderrTask = PumpAsync(
            process.StandardError,
            stderr,
            request,
            "stderr",
            "error",
            request.MaxOutputBytes,
            request.OnOutput);

        if (!string.IsNullOrEmpty(request.StandardInput))
        {
            await process.StandardInput.WriteAsync(request.StandardInput);
        }

        process.StandardInput.Close();

        var timedOut = false;
        var cancelled = false;

        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            cancelled = cancellationToken.IsCancellationRequested;
            KillTree(process);
            await WaitForExitQuietlyAsync(process);
        }

        // The streams end when the process (and its children) close them; bound the wait so a
        // detached child cannot hang the runner.
        await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(TimeSpan.FromSeconds(10)));
        await WaitForExitQuietlyAsync(process);

        var finishedAt = timeProvider.GetUtcNow().UtcDateTime;
        var redactions = request.Redactions ?? [];
        var stdoutText = LogRedactor.Redact(stdout.ToString(), redactions);
        var stderrText = LogRedactor.Redact(stderr.ToString(), redactions);

        var stdoutPath = Path.Combine(artifactDirectory, "stdout.log");
        var stderrPath = Path.Combine(artifactDirectory, "stderr.log");
        await File.WriteAllTextAsync(stdoutPath, stdoutText, Encoding.UTF8);
        await File.WriteAllTextAsync(stderrPath, stderrText, Encoding.UTF8);

        return new ProcessRunResult(
            process.HasExited ? process.ExitCode : -1,
            timedOut,
            cancelled,
            stdoutText,
            stderrText,
            stdoutPath,
            stderrPath,
            startedAt,
            finishedAt);
    }

    private static async Task PumpAsync(
        StreamReader reader,
        StringBuilder buffer,
        ProcessRunRequest request,
        string streamName,
        string level,
        int maxCharacters,
        Action<string, string>? onOutput)
    {
        var chunk = new char[4096];

        while (true)
        {
            var read = await reader.ReadAsync(chunk);
            if (read == 0)
            {
                return;
            }

            if (buffer.Length < maxCharacters)
            {
                var take = Math.Min(read, maxCharacters - buffer.Length);
                buffer.Append(chunk, 0, take);
            }

            if (onOutput is not null)
            {
                var text = new string(chunk, 0, read).TrimEnd('\r', '\n');
                if (text.Length > 0)
                {
                    onOutput(streamName, text);
                }
            }
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Process exited between the check and the kill.
        }
    }

    private static async Task WaitForExitQuietlyAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        }
        catch (OperationCanceledException)
        {
            // Give up waiting; the caller only needs a bounded result.
        }
    }
}
