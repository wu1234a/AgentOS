using AgentOS.Runner;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class WorkspacePolicyTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;

    public WorkspacePolicyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentos-workspace", Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "demo");
        Directory.CreateDirectory(_workspace);
    }

    [Fact]
    public void Configured_project_inside_a_root_is_allowed()
    {
        var decision = CreatePolicy(_workspace).Validate("demo");

        Assert.True(decision.Allowed);
        Assert.Equal(Path.GetFullPath(_workspace), decision.WorkspacePath);
    }

    [Fact]
    public void Unknown_project_is_rejected()
    {
        var decision = CreatePolicy(_workspace).Validate("other");

        Assert.False(decision.Allowed);
        Assert.StartsWith("project-not-allowed", decision.Reason);
    }

    [Fact]
    public void Project_outside_the_roots_is_rejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), "agentos-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);

        var decision = CreatePolicy(outside).Validate("demo");

        Assert.False(decision.Allowed);
        Assert.Equal("workspace-outside-roots", decision.Reason);
    }

    [Fact]
    public void Missing_workspace_is_rejected()
    {
        var decision = CreatePolicy(Path.Combine(_root, "does-not-exist")).Validate("demo");

        Assert.False(decision.Allowed);
        Assert.Equal("workspace-missing", decision.Reason);
    }

    [Fact]
    public void Path_traversal_is_rejected()
    {
        var traversal = Path.Combine(_root, "..", "escaped");
        Directory.CreateDirectory(traversal);

        var decision = CreatePolicy(traversal).Validate("demo");

        Assert.False(decision.Allowed);
        Assert.Equal("workspace-outside-roots", decision.Reason);
    }

    [SkippableFact]
    public void Symlinked_workspace_is_rejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), "agentos-link-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, "linked");

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            Skip.If(true, "Creating symlinks is not permitted in this environment.");
        }

        var decision = CreatePolicy(link).Validate("demo");

        Assert.False(decision.Allowed);
        Assert.True(decision.Reason is "workspace-outside-roots" or "workspace-reparse-point");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    private WorkspacePolicy CreatePolicy(string projectPath) => new(Options.Create(new RunnerOptions
    {
        WorkspaceRoots = [_root],
        Projects = [new ProjectWorkspace { Slug = "demo", Path = projectPath }]
    }));
}

public sealed class RunnerOptionsTests
{
    [Fact]
    public void Example_configuration_is_valid()
    {
        var options = new RunnerOptions
        {
            BaseUrl = "https://agentos.example.internal",
            WorkerCode = "local-runner-01",
            WorkspaceRoots = ["D:\\workspaces"],
            Projects = [new ProjectWorkspace { Slug = "demo", Path = "D:\\workspaces\\demo" }]
        };

        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    public void Invalid_base_url_is_reported(string baseUrl)
    {
        var options = new RunnerOptions
        {
            BaseUrl = baseUrl,
            WorkspaceRoots = ["D:\\workspaces"],
            Projects = [new ProjectWorkspace { Slug = "demo", Path = "D:\\workspaces\\demo" }]
        };

        Assert.Contains(options.Validate(), error => error.Contains("BaseUrl"));
    }

    [Fact]
    public void Missing_workspace_configuration_is_reported()
    {
        var options = new RunnerOptions { MaxConcurrentTasks = 0 };

        var errors = options.Validate();

        Assert.Contains(errors, error => error.Contains("MaxConcurrentTasks"));
        Assert.Contains(errors, error => error.Contains("WorkspaceRoots"));
        Assert.Contains(errors, error => error.Contains("Projects"));
    }
}
