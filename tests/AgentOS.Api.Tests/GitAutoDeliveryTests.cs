using System.Diagnostics;
using System.Net;
using System.Text;
using AgentOS.AgentAdapters.Processes;
using AgentOS.Runner.Delivery;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentOS.Api.Tests;

public sealed class GitAutoDeliveryTests
{
    [Fact]
    public async Task Commits_pushes_and_opens_a_pull_request()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentos-delivery-{Guid.NewGuid():N}");
        var remote = Path.Combine(root, "remote.git");
        var work = Path.Combine(root, "work");
        var tokenVariable = $"AGENTOS_TEST_TOKEN_{Guid.NewGuid():N}".ToUpperInvariant();

        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(work);
            Git(root, "init", "--bare", remote);
            Git(work, "init", "-b", "main");
            Git(work, "config", "user.name", "Test");
            Git(work, "config", "user.email", "test@example.com");
            File.WriteAllText(Path.Combine(work, "README.md"), "initial");
            Git(work, "add", "-A");
            Git(work, "commit", "-m", "initial");
            Git(work, "remote", "add", "origin", remote);
            Git(work, "push", "-u", "origin", "main");

            File.WriteAllText(Path.Combine(work, "feature.txt"), "delivered");
            Environment.SetEnvironmentVariable(tokenVariable, "test-token");

            var handler = new FakePullRequestHandler();
            var delivery = new GitAutoDelivery(
                new ExternalProcessRunner(TimeProvider.System),
                new FakeHttpClientFactory(handler),
                NullLogger<GitAutoDelivery>.Instance);

            var result = await delivery.DeliverAsync(
                new AutoDeliveryConfig(
                    "owner/repo",
                    "main",
                    "aos/test-delivery",
                    "test delivery",
                    "Test delivery",
                    "Automated test.",
                    "origin",
                    tokenVariable,
                    RequirePullRequest: true),
                work,
                (_, _) => { },
                CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("aos/test-delivery", result.Branch);
            Assert.NotNull(result.Commit);
            Assert.Equal("https://github.com/owner/repo/pull/1", result.PullRequestUrl);
            Assert.True(result.PullRequestCreated);
            Assert.Equal("main", Git(work, "rev-parse", "--abbrev-ref", "HEAD").Trim());
            Assert.Contains("aos/test-delivery", Git(work, "branch", "--list"));
            Assert.Contains("aos/test-delivery", Git(remote, "branch", "--list"));
            Assert.Contains("Test delivery", handler.RequestBody);
            Assert.Contains("Automated test.", handler.RequestBody);
            Assert.Contains("aos/test-delivery", handler.RequestBody);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenVariable, null);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Fails_when_delivery_is_enabled_but_the_workspace_has_no_changes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentos-empty-delivery-{Guid.NewGuid():N}");
        var work = Path.Combine(root, "work");

        try
        {
            Directory.CreateDirectory(work);
            Git(work, "init", "-b", "main");
            Git(work, "config", "user.name", "Test");
            Git(work, "config", "user.email", "test@example.com");
            File.WriteAllText(Path.Combine(work, "README.md"), "initial");
            Git(work, "add", "-A");
            Git(work, "commit", "-m", "initial");

            var delivery = new GitAutoDelivery(
                new ExternalProcessRunner(TimeProvider.System),
                new FakeHttpClientFactory(new FakePullRequestHandler()),
                NullLogger<GitAutoDelivery>.Instance);

            var result = await delivery.DeliverAsync(
                new AutoDeliveryConfig(
                    "owner/repo",
                    "main",
                    "aos/empty",
                    "empty delivery",
                    "Empty delivery",
                    "Automated test.",
                    "origin",
                    "AGENTOS_TEST_TOKEN",
                    RequirePullRequest: true),
                work,
                (_, _) => { },
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("delivery-no-changes", result.ErrorCode);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Delivers_from_a_linked_worktree_without_checking_out_the_base_branch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentos-linked-delivery-{Guid.NewGuid():N}");
        var remote = Path.Combine(root, "remote.git");
        var main = Path.Combine(root, "main");
        var linked = Path.Combine(root, "linked");
        var tokenVariable = $"AGENTOS_TEST_TOKEN_{Guid.NewGuid():N}".ToUpperInvariant();

        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(main);
            Git(root, "init", "--bare", remote);
            Git(main, "init", "-b", "main");
            Git(main, "config", "user.name", "Test");
            Git(main, "config", "user.email", "test@example.com");
            File.WriteAllText(Path.Combine(main, "README.md"), "initial");
            Git(main, "add", "-A");
            Git(main, "commit", "-m", "initial");
            Git(main, "remote", "add", "origin", remote);
            Git(main, "push", "-u", "origin", "main");
            Git(main, "worktree", "add", "-b", "aos/linked-delivery", linked, "main");

            File.WriteAllText(Path.Combine(linked, "feature.txt"), "delivered");
            Environment.SetEnvironmentVariable(tokenVariable, "test-token");

            var delivery = new GitAutoDelivery(
                new ExternalProcessRunner(TimeProvider.System),
                new FakeHttpClientFactory(new FakePullRequestHandler()),
                NullLogger<GitAutoDelivery>.Instance);

            var result = await delivery.DeliverAsync(
                new AutoDeliveryConfig(
                    "owner/repo",
                    "main",
                    "aos/linked-delivery",
                    "linked delivery",
                    "Linked delivery",
                    "Automated test.",
                    "origin",
                    tokenVariable,
                    RequirePullRequest: true),
                linked,
                (_, _) => { },
                CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("aos/linked-delivery", Git(linked, "rev-parse", "--abbrev-ref", "HEAD").Trim());
            Assert.Contains("aos/linked-delivery", Git(remote, "branch", "--list"));
            Git(main, "worktree", "remove", "--force", linked);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenVariable, null);
            TryDelete(root);
        }
    }

    private static string Git(string workingDirectory, params string[] arguments)
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }

        return output;
    }

    private static void TryDelete(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakePullRequestHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"html_url":"https://github.com/owner/repo/pull/1"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
