using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentOS.AgentAdapters.Processes;

namespace AgentOS.Runner.Delivery;

public sealed record AutoDeliveryResult(
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    string? Branch = null,
    string? Commit = null,
    string? PullRequestUrl = null,
    bool PullRequestCreated = false,
    string? PushUrl = null)
{
    public static AutoDeliveryResult Ok(
        string? branch = null,
        string? commit = null,
        string? pullRequestUrl = null,
        bool pullRequestCreated = false,
        string? pushUrl = null)
        => new(true, Branch: branch, Commit: commit, PullRequestUrl: pullRequestUrl,
            PullRequestCreated: pullRequestCreated, PushUrl: pushUrl);

    public static AutoDeliveryResult Failed(string code, string message, string? branch = null, string? commit = null)
        => new(false, code, message, branch, commit);
}

public interface IAutoDelivery
{
    Task<AutoDeliveryResult> DeliverAsync(
        AutoDeliveryConfig config,
        string workspacePath,
        Action<string, string> log,
        CancellationToken cancellationToken);
}

/// <summary>
/// Commits the validated workspace changes on a task branch, pushes it, and optionally opens a
/// pull request. The GitHub token is read from the process environment and is never logged.
/// </summary>
public sealed class GitAutoDelivery(
    IProcessRunner processRunner,
    IHttpClientFactory httpClientFactory,
    ILogger<GitAutoDelivery> logger) : IAutoDelivery
{
    private const int GitTimeoutSeconds = 120;
    private const int MaxGitOutputBytes = 512 * 1024;

    public async Task<AutoDeliveryResult> DeliverAsync(
        AutoDeliveryConfig config,
        string workspacePath,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        try
        {
            return await DeliverCoreAsync(config, workspacePath, log, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DeliveryException exception)
        {
            log("error", $"delivery failed: {exception.Code} {exception.Message}");
            return AutoDeliveryResult.Failed(exception.Code, exception.Message);
        }
        catch (TimeoutException exception)
        {
            log("error", $"delivery timed out: {exception.Message}");
            return AutoDeliveryResult.Failed("delivery-timeout", exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Delivery failed unexpectedly.");
            return AutoDeliveryResult.Failed("delivery-error", "Delivery failed unexpectedly.");
        }
    }

    private async Task<AutoDeliveryResult> DeliverCoreAsync(
        AutoDeliveryConfig config,
        string workspacePath,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        if (!await IsRepositoryRootAsync(workspacePath, log, cancellationToken))
        {
            return AutoDeliveryResult.Failed(
                "delivery-not-a-repository",
                "The task workspace is not the root of a git repository.");
        }

        var status = await RunGitAsync(workspacePath, ["status", "--porcelain", "--untracked-files=all"], log, cancellationToken);
        EnsureSuccess(status, "delivery-status-failed", "Could not inspect the git working tree.");

        if (string.IsNullOrWhiteSpace(status.Stdout))
        {
            // Some agents commit their own work. A clean tree is still deliverable when the
            // current branch has commits that are not yet on the base branch.
            var ahead = await RunGitAsync(
                workspacePath,
                ["rev-list", "--count", $"origin/{config.BaseBranch}..HEAD"],
                log,
                cancellationToken);
            var hasCommittedWork = false;
            if (ahead.ExitCode == 0
                && int.TryParse(ahead.Stdout.Trim(), out var aheadCount))
            {
                hasCommittedWork = aheadCount > 0;
            }

            if (!hasCommittedWork)
            {
                log("error", "delivery is enabled but the task produced no changes");
                return AutoDeliveryResult.Failed(
                    "delivery-no-changes",
                    "Delivery is enabled but the task produced no changes, so no branch or pull request could be created.");
            }

            log("info", $"delivery: using {ahead.Stdout.Trim()} already-committed change(s) on the task branch");
        }

        var currentBranchResult = await RunGitAsync(workspacePath, ["rev-parse", "--abbrev-ref", "HEAD"], log, cancellationToken);
        EnsureSuccess(currentBranchResult, "delivery-branch-failed", "Could not read the current git branch.");
        var currentBranch = currentBranchResult.Stdout.Trim();

        if (string.Equals(currentBranch, "HEAD", StringComparison.Ordinal))
        {
            return AutoDeliveryResult.Failed(
                "delivery-detached-head",
                "The workspace is in detached HEAD state; delivery requires a branch.");
        }

        if (!string.Equals(currentBranch, config.Branch, StringComparison.Ordinal))
        {
            var exists = await LocalBranchExistsAsync(workspacePath, config.Branch, log, cancellationToken);
            var checkout = exists
                ? await RunGitAsync(workspacePath, ["checkout", config.Branch], log, cancellationToken)
                : await RunGitAsync(workspacePath, ["checkout", "-b", config.Branch], log, cancellationToken);
            EnsureSuccess(checkout, "delivery-checkout-failed", $"Could not switch to delivery branch '{config.Branch}'.");
        }

        var add = await RunGitAsync(workspacePath, ["add", "--all"], log, cancellationToken);
        EnsureSuccess(add, "delivery-add-failed", "Could not stage the task changes.");

        var staged = await RunGitAsync(workspacePath, ["diff", "--cached", "--quiet"], log, cancellationToken);
        if (staged.ExitCode == 1)
        {
            var commit = await RunGitAsync(
                workspacePath,
                ["-c", "user.name=AgentOS", "-c", "user.email=agentos@local", "commit", "-m", config.CommitMessage],
                log,
                cancellationToken);
            if (commit.ExitCode != 0)
            {
                return AutoDeliveryResult.Failed(
                    "delivery-commit-failed",
                    Truncate(commit.Stderr, 1000));
            }
        }
        else if (staged.ExitCode != 0)
        {
            return AutoDeliveryResult.Failed(
                "delivery-commit-failed",
                "Could not determine whether staged changes exist.");
        }

        var head = await RunGitAsync(workspacePath, ["rev-parse", "HEAD"], log, cancellationToken);
        EnsureSuccess(head, "delivery-revision-failed", "Could not read the delivery commit.");
        var commitSha = head.Stdout.Trim();

        var push = await RunGitAsync(
            workspacePath,
            ["push", "-u", config.Remote, config.Branch],
            log,
            cancellationToken,
            timeoutSeconds: GitTimeoutSeconds);
        if (push.ExitCode != 0)
        {
            return AutoDeliveryResult.Failed(
                "delivery-push-failed",
                Truncate(push.Stderr, 1000),
                config.Branch,
                commitSha);
        }

        var pushUrl = $"https://github.com/{config.Repository}/tree/{config.Branch}";
        log("info", $"delivery: pushed {config.Branch} at {commitSha}");

        if (!config.RequirePullRequest)
        {
            await ReturnToBaseAsync(workspacePath, config, log, cancellationToken);
            return AutoDeliveryResult.Ok(config.Branch, commitSha, pushUrl: pushUrl);
        }

        var token = Environment.GetEnvironmentVariable(config.TokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(token))
        {
            log("warn", $"delivery: {config.TokenEnvironmentVariable} is not set; push succeeded but no pull request was created");
            await ReturnToBaseAsync(workspacePath, config, log, cancellationToken);
            return AutoDeliveryResult.Failed(
                "delivery-token-missing",
                $"The branch was pushed, but {config.TokenEnvironmentVariable} is not set so no pull request could be created.",
                config.Branch,
                commitSha) with { PushUrl = pushUrl };
        }

        var pullRequest = await CreatePullRequestAsync(config, token, cancellationToken);
        await ReturnToBaseAsync(workspacePath, config, log, cancellationToken);

        if (!pullRequest.Success)
        {
            return AutoDeliveryResult.Failed(
                pullRequest.ErrorCode ?? "delivery-pr-failed",
                pullRequest.ErrorMessage ?? "Could not create the pull request.",
                config.Branch,
                commitSha) with { PullRequestUrl = pullRequest.Url, PushUrl = pushUrl };
        }

        return AutoDeliveryResult.Ok(
            config.Branch,
            commitSha,
            pullRequest.Url,
            pullRequestCreated: pullRequest.Created,
            pushUrl: pushUrl);
    }

    private async Task<bool> IsRepositoryRootAsync(
        string workspacePath,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(workspacePath, ["rev-parse", "--show-toplevel"], log, cancellationToken);
        if (result.ExitCode != 0)
        {
            return false;
        }

        static string Normalize(string value)
            => Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim()));

        return string.Equals(
            Normalize(result.Stdout),
            Normalize(workspacePath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private async Task<bool> LocalBranchExistsAsync(
        string workspacePath,
        string branch,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            workspacePath,
            ["show-ref", "--verify", "--quiet", $"refs/heads/{branch}"],
            log,
            cancellationToken);
        return result.ExitCode == 0;
    }

    private async Task ReturnToBaseAsync(
        string workspacePath,
        AutoDeliveryConfig config,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        if (await IsLinkedWorktreeAsync(workspacePath, log, cancellationToken))
        {
            // The primary checkout owns the base branch; a linked worktree cannot check it out.
            // The worktree manager removes this isolated checkout after delivery is recorded.
            log("info", $"delivery: linked worktree remains on {config.Branch}");
            return;
        }

        var checkout = await RunGitAsync(workspacePath, ["checkout", config.BaseBranch], log, cancellationToken);
        if (checkout.ExitCode != 0)
        {
            logger.LogWarning(
                "Delivery of {Branch} succeeded, but the workspace could not return to {BaseBranch}.",
                config.Branch,
                config.BaseBranch);
        }
    }

    private async Task<bool> IsLinkedWorktreeAsync(
        string workspacePath,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        var gitDirectory = await RunGitAsync(workspacePath, ["rev-parse", "--git-dir"], log, cancellationToken);
        var commonDirectory = await RunGitAsync(workspacePath, ["rev-parse", "--git-common-dir"], log, cancellationToken);

        if (gitDirectory.ExitCode != 0
            || commonDirectory.ExitCode != 0
            || string.IsNullOrWhiteSpace(gitDirectory.Stdout)
            || string.IsNullOrWhiteSpace(commonDirectory.Stdout))
        {
            return false;
        }

        static string Normalize(string workspace, string value)
            => Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(workspace, value.Trim())));

        return !string.Equals(
            Normalize(workspacePath, gitDirectory.Stdout),
            Normalize(workspacePath, commonDirectory.Stdout),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private async Task<PullRequestResult> CreatePullRequestAsync(
        AutoDeliveryConfig config,
        string token,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("agentos-github");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.github.com/repos/{config.Repository}/pulls");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("AgentOS/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                title = config.PullRequestTitle,
                head = config.Branch,
                @base = config.BaseBranch,
                body = config.PullRequestBody
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
        {
            return new PullRequestResult(true, true, ReadHtmlUrl(responseBody), null, null);
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var existing = await FindOpenPullRequestAsync(client, config, token, cancellationToken);
            if (existing is not null)
            {
                return new PullRequestResult(true, false, existing, null, null);
            }
        }

        return new PullRequestResult(
            false,
            false,
            null,
            "delivery-pr-failed",
            $"GitHub returned {(int)response.StatusCode}: {Truncate(responseBody, 500)}");
    }

    private static async Task<string?> FindOpenPullRequestAsync(
        HttpClient client,
        AutoDeliveryConfig config,
        string token,
        CancellationToken cancellationToken)
    {
        var head = Uri.EscapeDataString(config.Branch);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{config.Repository}/pulls?state=open&head={head}&base={Uri.EscapeDataString(config.BaseBranch)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("AgentOS/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        return document.RootElement[0].TryGetProperty("html_url", out var url) ? url.GetString() : null;
    }

    private static string? ReadHtmlUrl(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.TryGetProperty("html_url", out var url) ? url.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ProcessRunResult> RunGitAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        Action<string, string> log,
        CancellationToken cancellationToken,
        int timeoutSeconds = 60)
    {
        var result = await processRunner.RunAsync(
            new ProcessRunRequest(
                "git",
                arguments,
                workspacePath,
                null,
                TimeSpan.FromSeconds(timeoutSeconds),
                BuildGitEnvironment(),
                MaxGitOutputBytes,
                OnOutput: (_, line) => log("info", $"git: {line}")),
            cancellationToken);

        if (result.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.TimedOut)
        {
            throw new TimeoutException($"git {string.Join(' ', arguments)} timed out after {timeoutSeconds}s.");
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildGitEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never"
        };

        foreach (var name in new[]
                 {
                     "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE",
                     "COMSPEC", "PATHEXT", "HOME", "LANG", "LC_ALL",
                     "ALLUSERSPROFILE", "LOCALAPPDATA", "APPDATA", "PROGRAMDATA",
                     "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY"
                 })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    private static void EnsureSuccess(ProcessRunResult result, string code, string message)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        throw new DeliveryException(code, $"{message} {Truncate(result.Stderr, 500)}".Trim());
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private sealed record PullRequestResult(
        bool Success,
        bool Created,
        string? Url,
        string? ErrorCode,
        string? ErrorMessage);
}

public sealed class DeliveryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
