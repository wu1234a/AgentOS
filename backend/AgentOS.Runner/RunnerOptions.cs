namespace AgentOS.Runner;

/// <summary>Local runner configuration. Secrets come from environment variables or user secrets.</summary>
public sealed class RunnerOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>Task handler to use: <c>demo</c>, <c>codex-cli</c>, <c>openai-compatible</c>, or <c>multi</c>.</summary>
    public string AgentAdapter { get; set; } = "demo";

    public string WorkerCode { get; set; } = "local-runner";

    public string DisplayName { get; set; } = "Local Runner";

    public string HostName { get; set; } = Environment.MachineName;

    public string Platform { get; set; } = "Windows";

    public string Architecture { get; set; } = "x64";

    public string Version { get; set; } = "1.0.0";

    /// <summary>Optional root for isolated per-Attempt Git worktrees.</summary>
    public string? WorktreeRoot { get; set; }

    /// <summary>Directories that may contain project workspaces.</summary>
    public string[] WorkspaceRoots { get; set; } = [];

    /// <summary>Project slug → workspace path. Only these projects may be executed.</summary>
    public ProjectWorkspace[] Projects { get; set; } = [];

    public int PollIntervalSeconds { get; set; } = 5;

    public int HeartbeatIntervalSeconds { get; set; } = 20;

    public int MaxConcurrentTasks { get; set; } = 2;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int MaxLogEntriesPerBatch { get; set; } = 50;

    /// <summary>Bootstrap key used once at registration; not needed afterwards.</summary>
    public string? RegistrationKey { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Development fallback when no client credentials are configured.</summary>
    public string? DevApiKey { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("BaseUrl must be an absolute http(s) URL.");
        }

        if (string.IsNullOrWhiteSpace(WorkerCode) || WorkerCode.Length > 80)
        {
            errors.Add("WorkerCode is required and must be 80 characters or fewer.");
        }

        if (MaxConcurrentTasks is < 1 or > 32)
        {
            errors.Add("MaxConcurrentTasks must be between 1 and 32.");
        }

        if (PollIntervalSeconds is < 1 or > 300)
        {
            errors.Add("PollIntervalSeconds must be between 1 and 300.");
        }

        if (HeartbeatIntervalSeconds is < 5 or > 300)
        {
            errors.Add("HeartbeatIntervalSeconds must be between 5 and 300.");
        }

        if (AgentAdapter is not ("demo" or "codex-cli" or "openai-compatible" or "multi"))
        {
            errors.Add("AgentAdapter must be demo, codex-cli, openai-compatible, or multi.");
        }

        if (WorkspaceRoots.Length == 0)
        {
            errors.Add("WorkspaceRoots must contain at least one directory.");
        }

        if (Projects.Length == 0)
        {
            errors.Add("Projects must map at least one project slug to a workspace path.");
        }

        return errors;
    }
}

public sealed class ProjectWorkspace
{
    public string Slug { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}
