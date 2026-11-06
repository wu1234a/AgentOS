using AgentOS.Application.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOS.Infrastructure.Skills;

/// <summary>
/// Reads skill markdown from the configured skills root. The relative path is normalized and
/// must stay under the root; oversized or missing files are reported as absent.
/// </summary>
public sealed class SkillContentReader(
    IOptions<SkillContentOptions> options,
    ILogger<SkillContentReader> logger) : ISkillContentReader
{
    public async Task<string?> ReadAsync(string? relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var rootPath = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        string fullPath;
        string root;

        try
        {
            root = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath));

            if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(candidate, root, StringComparison.Ordinal))
            {
                logger.LogWarning("Skill path '{Path}' escapes the skills root and was ignored.", relativePath);
                return null;
            }

            fullPath = candidate;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            logger.LogWarning(exception, "Skill path '{Path}' is invalid.", relativePath);
            return null;
        }

        if (HasReparsePoint(root, fullPath))
        {
            logger.LogWarning("Skill file '{Path}' or one of its parents is a symbolic link and was ignored.", fullPath);
            return null;
        }

        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            logger.LogWarning("Skill file '{Path}' does not exist.", fullPath);
            return null;
        }

        if (info.Length > options.Value.MaxBytes)
        {
            logger.LogWarning("Skill file '{Path}' exceeds {MaxBytes} bytes and was ignored.", fullPath, options.Value.MaxBytes);
            return null;
        }

        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    private static bool HasReparsePoint(string root, string target)
    {
        var file = new FileInfo(target);

        if (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        var directory = file.Directory;

        while (directory is not null)
        {
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            if (string.Equals(
                    directory.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.Ordinal))
            {
                return false;
            }

            directory = directory.Parent;
        }

        return true;
    }
}
