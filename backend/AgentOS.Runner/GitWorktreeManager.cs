using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AgentOS.Runner;

public sealed record WorktreeRequest(Guid CodingRunId, string BaseRevision);

public sealed record WorktreeContext(
    string Path,
    string RepositoryPath,
    string Branch,
    string WorktreeId,
    string BaseRevision);

public sealed record WorktreeCreateResult(
    bool Success,
    WorktreeContext? Context,
    string? ErrorCode,
    string? ErrorMessage);

public interface IWorktreeManager
{
    bool TryParseRequest(string inputJson, out WorktreeRequest? request, out string? error);

    Task<WorktreeCreateResult> CreateAsync(
        string projectPath,
        WorktreeRequest request,
        CancellationToken cancellationToken);

    /// <summary>Creates a worktree from the project's current HEAD when the task did not specify one.</summary>
    Task<WorktreeCreateResult> CreateAutomaticAsync(
        string projectPath,
        Guid codingRunId,
        CancellationToken cancellationToken);

    Task RemoveAsync(WorktreeContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Creates one isolated Git worktree per Coding Run Attempt. Worktrees are created from the
/// approved full commit id, never fetch, and are only removed after verifying ownership.
/// </summary>
public sealed class GitWorktreeManager(
    IOptions<RunnerOptions> options,
    ILogger<GitWorktreeManager> logger) : IWorktreeManager
{
    private const int GitTimeoutSeconds = 60;

    public bool TryParseRequest(string inputJson, out WorktreeRequest? request, out string? error)
    {
        request = null;
        error = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("workspace", out var workspace)
                || workspace.ValueKind != JsonValueKind.Object
                || !workspace.TryGetProperty("mode", out var mode)
                || mode.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            if (!string.Equals(mode.GetString(), "git-worktree", StringComparison.OrdinalIgnoreCase))
            {
                error = "workspace mode is not supported.";
                return true;
            }

            if (!document.RootElement.TryGetProperty("codingRunId", out var runValue)
                || runValue.ValueKind != JsonValueKind.String
                || !Guid.TryParse(runValue.GetString(), out var codingRunId))
            {
                error = "codingRunId is required for git-worktree mode.";
                return true;
            }

            if (!document.RootElement.TryGetProperty("baseRevision", out var revisionValue)
                || revisionValue.ValueKind != JsonValueKind.String
                || revisionValue.GetString() is not { } revision
                || !IsFullCommit(revision))
            {
                error = "baseRevision must be a full 40 or 64 character hex commit id.";
                return true;
            }

            request = new WorktreeRequest(codingRunId, revision.ToLowerInvariant());
            return true;
        }
    }

    public async Task<WorktreeCreateResult> CreateAutomaticAsync(
        string projectPath,
        Guid codingRunId,
        CancellationToken cancellationToken)
    {
        var head = await RunGitAsync(projectPath, ["rev-parse", "HEAD"], cancellationToken);
        if (!head.Success || !IsFullCommit(head.Output.Trim()))
        {
            return Failure("worktree-no-head", "Project workspace has no reachable HEAD commit.");
        }

        return await CreateAsync(
            projectPath,
            new WorktreeRequest(codingRunId, head.Output.Trim().ToLowerInvariant()),
            cancellationToken);
    }

    public async Task<WorktreeCreateResult> CreateAsync(
        string projectPath,
        WorktreeRequest request,
        CancellationToken cancellationToken)
    {
        var root = options.Value.WorktreeRoot;
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
        {
            return Failure("worktree-config", "Runner:WorktreeRoot must be an absolute path.");
        }

        string rootPath;
        try
        {
            rootPath = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Directory.CreateDirectory(rootPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure("worktree-config", "Worktree root is not usable.");
        }

        var repository = await RunGitAsync(projectPath, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (!repository.Success)
        {
            return Failure("worktree-not-a-repository", "Project workspace is not a Git repository.");
        }

        string repositoryPath;
        try
        {
            repositoryPath = Path.GetFullPath(repository.Output.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure("worktree-not-a-repository", "Repository path is invalid.");
        }
        if (IsOverlapping(rootPath, repositoryPath))
        {
            return Failure("worktree-config", "Worktree root must not overlap the shared repository.");
        }

        var reachable = await RunGitAsync(
            repositoryPath,
            ["cat-file", "-e", $"{request.BaseRevision}^{{commit}}"],
            cancellationToken);
        if (!reachable.Success)
        {
            return Failure("base-revision-unavailable", "Base revision is not a local commit.");
        }

        var worktreeId = $"aosc-{Guid.NewGuid():N}";
        var repoKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(repositoryPath.ToLowerInvariant())))[..12].ToLowerInvariant();
        var worktreePath = Path.Combine(rootPath, repoKey, worktreeId);
        var branch = $"aos/coding-run/{worktreeId}";

        if (HasReparsePoint(rootPath)
            || HasReparsePoint(Path.Combine(rootPath, repoKey))
            || Directory.Exists(worktreePath)
            || File.Exists(worktreePath))
        {
            return Failure("worktree-conflict", "Worktree path already exists.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("worktree-create-failed", "Worktree directory could not be created.");
        }

        var created = await RunGitAsync(
            repositoryPath,
            ["worktree", "add", "-b", branch, worktreePath, request.BaseRevision],
            cancellationToken);

        if (!created.Success)
        {
            await TryRemoveAsync(repositoryPath, worktreePath, branch);
            return Failure("worktree-create-failed", "Git worktree add failed.");
        }

        var head = await RunGitAsync(worktreePath, ["rev-parse", "HEAD"], cancellationToken);
        if (!head.Success || !string.Equals(head.Output.Trim(), request.BaseRevision, StringComparison.OrdinalIgnoreCase))
        {
            await TryRemoveAsync(repositoryPath, worktreePath, branch);
            return Failure("base-revision-mismatch", "Worktree HEAD does not match baseRevision.");
        }

        logger.LogInformation(
            "Created worktree {WorktreeId} for coding run {RunId}.",
            worktreeId,
            request.CodingRunId);

        return new WorktreeCreateResult(
            true,
            new WorktreeContext(worktreePath, repositoryPath, branch, worktreeId, request.BaseRevision),
            null,
            null);
    }

    public async Task RemoveAsync(WorktreeContext context, CancellationToken cancellationToken)
    {
        var root = options.Value.WorktreeRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        try
        {
            var rootPath = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var worktreePath = Path.GetFullPath(context.Path);
            if (!IsUnder(rootPath, worktreePath) || !context.Branch.StartsWith("aos/coding-run/", StringComparison.Ordinal))
            {
                logger.LogWarning("Refusing to remove worktree outside the configured root.");
                return;
            }

            var status = await RunGitAsync(worktreePath, ["status", "--porcelain"], cancellationToken);
            var ahead = await RunGitAsync(
                worktreePath,
                ["rev-list", "--count", $"{context.BaseRevision}..HEAD"],
                cancellationToken);

            if (!status.Success
                || !ahead.Success
                || !string.IsNullOrWhiteSpace(status.Output)
                || !int.TryParse(ahead.Output.Trim(), out var commitCount))
            {
                logger.LogWarning(
                    "Worktree {WorktreeId} contains unarchived changes or commits; retaining it.",
                    context.WorktreeId);
                return;
            }

            var hasUnpushedCommits = false;
            if (commitCount > 0)
            {
                var unpushed = await RunGitAsync(
                    worktreePath,
                    ["rev-list", "--count", "@{u}..HEAD"],
                    cancellationToken);
                hasUnpushedCommits = !unpushed.Success
                    || !int.TryParse(unpushed.Output.Trim(), out var unpushedCount)
                    || unpushedCount > 0;
            }

            if (hasUnpushedCommits)
            {
                logger.LogWarning(
                    "Worktree {WorktreeId} contains unpushed commits; retaining it.",
                    context.WorktreeId);
                return;
            }

            var removed = await RunGitAsync(
                context.RepositoryPath,
                ["worktree", "remove", "--force", worktreePath],
                cancellationToken);

            if (!removed.Success)
            {
                logger.LogWarning("Worktree {WorktreeId} could not be removed; keeping it.", context.WorktreeId);
                return;
            }

            var branch = await RunGitAsync(
                context.RepositoryPath,
                ["branch", "-D", context.Branch],
                cancellationToken);
            if (!branch.Success)
            {
                logger.LogWarning("Temporary branch for worktree {WorktreeId} could not be deleted.", context.WorktreeId);
            }

            logger.LogInformation("Removed worktree {WorktreeId}.", context.WorktreeId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Worktree cleanup failed for {WorktreeId}.", context.WorktreeId);
        }
    }

    private static bool IsFullCommit(string value)
        => Regex.IsMatch(value, "^[0-9a-fA-F]{40}$|^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    private static bool IsUnder(string root, string candidate)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar);
        return candidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return Directory.Exists(path)
                && new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsOverlapping(string root, string repository)
        => IsUnder(root, repository) || IsUnder(repository, root);

    private async Task TryRemoveAsync(string repositoryPath, string worktreePath, string branch)
    {
        await RunGitAsync(repositoryPath, ["worktree", "remove", "--force", worktreePath], CancellationToken.None);
        await RunGitAsync(repositoryPath, ["branch", "-D", branch], CancellationToken.None);
    }

    private static async Task<(bool Success, string Output, string Error)> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
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
            if (!process.Start())
            {
                return (false, string.Empty, "git did not start.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (false, string.Empty, "git could not start.");
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

        return process.ExitCode == 0
            ? (true, await stdout, await stderr)
            : (false, await stdout, await stderr);
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

    private static WorktreeCreateResult Failure(string code, string message)
        => new(false, null, code, message);
}
