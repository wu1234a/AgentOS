using System.Diagnostics;
using AgentOS.Runner;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class GitWorktreeManagerTests
{
    [Fact]
    public async Task Creates_isolated_worktrees_at_the_base_commit_and_removes_them()
    {
        using var repo = new TempGitRepository();
        var head = repo.Commit("initial");
        var root = Path.Combine(Path.GetTempPath(), $"agentos-worktrees-{Guid.NewGuid():N}");
        var manager = CreateManager(root);
        var runId = Guid.NewGuid();

        var first = await manager.CreateAsync(
            repo.Path,
            new WorktreeRequest(runId, head),
            CancellationToken.None);
        var second = await manager.CreateAsync(
            repo.Path,
            new WorktreeRequest(runId, head),
            CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEqual(first.Context!.Path, second.Context!.Path);
        Assert.Equal(head, RunGit(first.Context.Path, "rev-parse", "HEAD").Trim());
        Assert.Equal(head, RunGit(second.Context.Path, "rev-parse", "HEAD").Trim());

        await manager.RemoveAsync(first.Context, CancellationToken.None);
        await manager.RemoveAsync(second.Context, CancellationToken.None);

        Assert.False(Directory.Exists(first.Context.Path));
        Assert.False(Directory.Exists(second.Context.Path));
        Assert.Equal(string.Empty, RunGit(repo.Path, "branch", "--list", "aos/coding-run/*").Trim());
    }

    [Fact]
    public async Task Rejects_an_unknown_base_revision()
    {
        using var repo = new TempGitRepository();
        repo.Commit("initial");
        var manager = CreateManager(Path.Combine(Path.GetTempPath(), $"agentos-worktrees-{Guid.NewGuid():N}"));

        var result = await manager.CreateAsync(
            repo.Path,
            new WorktreeRequest(Guid.NewGuid(), new string('a', 40)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("base-revision-unavailable", result.ErrorCode);
    }

    [Fact]
    public void Parses_only_git_worktree_requests()
    {
        var manager = CreateManager(Path.Combine(Path.GetTempPath(), $"agentos-worktrees-{Guid.NewGuid():N}"));
        var runId = Guid.NewGuid();
        var input = "{\"codingRunId\":\"" + runId + "\",\"baseRevision\":\"" + new string('a', 40) + "\",\"workspace\":{\"mode\":\"git-worktree\"}}";

        var parsed = manager.TryParseRequest(input, out var request, out var error);

        Assert.True(parsed);
        Assert.NotNull(request);
        Assert.Null(error);
        Assert.Equal(runId, request!.CodingRunId);
    }

    private static GitWorktreeManager CreateManager(string root) => new(
        Options.Create(new RunnerOptions { WorktreeRoot = root }),
        NullLogger<GitWorktreeManager>.Instance);

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return output;
    }

    private sealed class TempGitRepository : IDisposable
    {
        public TempGitRepository()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agentos-repo-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            RunGit(Path, "init");
            RunGit(Path, "config", "user.email", "test@example.com");
            RunGit(Path, "config", "user.name", "Test");
        }

        public string Path { get; }

        public string Commit(string message)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "file.txt"), message);
            RunGit(Path, "add", "file.txt");
            RunGit(Path, "commit", "-m", message);
            return RunGit(Path, "rev-parse", "HEAD").Trim();
        }

        public void Dispose()
        {
            try
            {
                if (!Directory.Exists(Path))
                {
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch (IOException)
                    {
                    }
                }

                Directory.Delete(Path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Temp cleanup is best effort; the OS may still hold worktree metadata handles.
            }
        }
    }
}
