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

        try
        {
            var root = Path.GetFullPath(rootPath)
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
}
