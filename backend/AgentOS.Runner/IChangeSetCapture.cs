using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AgentOS.Runner;

public sealed record ChangeSetArtifact(
    string DiffPath,
    string MetadataPath,
    string BaseRevision,
    string HeadRevision,
    IReadOnlyList<string> Files);

public sealed record ChangeSetCaptureResult(
    bool Success,
    ChangeSetArtifact? Artifact,
    string? ErrorCode,
    string? ErrorMessage);

public interface IChangeSetCapture
{
    Task<ChangeSetCaptureResult> CaptureAsync(
        string workspacePath,
        string baseRevision,
        CancellationToken cancellationToken);
}

/// <summary>
/// Captures the immutable diff for an approved Coding Run: HEAD, changed files and a unified
/// diff from the requested base revision. The diff is written to temporary files that the
/// existing artifact uploader persists.
/// </summary>
public sealed class GitChangeSetCapture : IChangeSetCapture
{
    private const int MaxDiffBytes = 1_000_000;
    private const int GitTimeoutSeconds = 60;

    public async Task<ChangeSetCaptureResult> CaptureAsync(
        string workspacePath,
        string baseRevision,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"agentos-changeset-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var head = await RunGitAsync(workspacePath, ["rev-parse", "HEAD"], cancellationToken);
            if (!head.Success)
            {
                return Failure("changeset-error", "Could not resolve HEAD: " + head.Error);
            }

            var reachable = await RunGitAsync(
                workspacePath,
                ["cat-file", "-e", $"{baseRevision}^{{commit}}"],
                cancellationToken);
            if (!reachable.Success)
            {
                return Failure("changeset-error", "Base revision is not a reachable commit.");
            }

            var names = await RunGitAsync(
                workspacePath,
                ["diff", "--name-status", $"{baseRevision}..HEAD"],
                cancellationToken);
            if (!names.Success)
            {
                return Failure("changeset-error", "Could not list changed files: " + names.Error);
            }

            var diff = await RunGitAsync(
                workspacePath,
                ["diff", "--no-ext-diff", "--unified=3", $"{baseRevision}..HEAD"],
                cancellationToken);
            if (!diff.Success)
            {
                return Failure("changeset-error", "Could not capture diff: " + diff.Error);
            }

            if (Encoding.UTF8.GetByteCount(diff.Output) > MaxDiffBytes)
            {
                return Failure("changeset-too-large", "Change set exceeds the 1 MB review limit.");
            }

            var files = names.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('\t').Last().Trim())
                .Where(line => line.Length > 0)
                .ToList();

            if (files.Count == 0 && string.IsNullOrWhiteSpace(diff.Output))
            {
                return Failure("changeset-empty", "The change set is empty.");
            }

            var diffPath = Path.Combine(directory, "changeset.diff");
            var metadataPath = Path.Combine(directory, "changeset.json");
            await File.WriteAllTextAsync(diffPath, diff.Output, Encoding.UTF8, cancellationToken);
            await File.WriteAllTextAsync(
                metadataPath,
                JsonSerializer.Serialize(new
                {
                    baseRevision,
                    headRevision = head.Output.Trim(),
                    files
                }),
                Encoding.UTF8,
                cancellationToken);

            return new ChangeSetCaptureResult(
                true,
                new ChangeSetArtifact(diffPath, metadataPath, baseRevision, head.Output.Trim(), files),
                null,
                null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("changeset-error", "Could not write change set: " + exception.Message);
        }
    }

    private static async Task<(bool Success, string Output, string Error)> RunGitAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (false, string.Empty, "git could not start: " + exception.Message);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(GitTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return (false, string.Empty, "git command timed out.");
        }

        var output = await stdout;
        var error = await stderr;
        return process.ExitCode == 0
            ? (true, output, error)
            : (false, output, string.IsNullOrWhiteSpace(error) ? "git failed." : error.Trim());
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
        }
    }

    private static ChangeSetCaptureResult Failure(string code, string message)
        => new(false, null, code, message);
}
