using Microsoft.Extensions.Options;

namespace AgentOS.Runner;

/// <summary>Result of validating a task's project against the runner's workspace policy.</summary>
public sealed record WorkspaceDecision(bool Allowed, string? Reason, string? WorkspacePath)
{
    public static WorkspaceDecision Allow(string path) => new(true, null, path);

    public static WorkspaceDecision Deny(string reason) => new(false, reason, null);
}

/// <summary>
/// Enforces the project and workspace whitelist: only configured projects may run, their paths
/// must live under a configured root, and no path component may be a reparse point.
/// </summary>
public sealed class WorkspacePolicy(IOptions<RunnerOptions> options)
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    public WorkspaceDecision Validate(string projectSlug)
    {
        var configured = options.Value.Projects
            .FirstOrDefault(project => string.Equals(project.Slug, projectSlug, StringComparison.OrdinalIgnoreCase));

        if (configured is null || string.IsNullOrWhiteSpace(configured.Path))
        {
            return WorkspaceDecision.Deny($"project-not-allowed:{projectSlug}");
        }

        string projectPath;
        try
        {
            projectPath = Path.GetFullPath(configured.Path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return WorkspaceDecision.Deny("invalid-workspace-path");
        }

        var root = options.Value.WorkspaceRoots
            .Select(TryNormalize)
            .FirstOrDefault(candidate => candidate is not null && IsUnder(candidate, projectPath));

        if (root is null)
        {
            return WorkspaceDecision.Deny("workspace-outside-roots");
        }

        if (!Directory.Exists(projectPath))
        {
            return WorkspaceDecision.Deny("workspace-missing");
        }

        if (HasReparsePoint(root, projectPath))
        {
            return WorkspaceDecision.Deny("workspace-reparse-point");
        }

        return WorkspaceDecision.Allow(projectPath);
    }

    private static string? TryNormalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsUnder(string root, string candidate)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedRoot, normalizedCandidate, PathComparison))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            PathComparison);
    }

    private static bool HasReparsePoint(string root, string target)
    {
        var current = new DirectoryInfo(target);

        while (current is not null)
        {
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            if (string.Equals(
                    current.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar),
                    PathComparison))
            {
                return false;
            }

            current = current.Parent;
        }

        return false;
    }
}
