using System.Security.Cryptography;
using System.Text;
using AgentOS.Application.Skills;
using AgentOS.Application.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOS.Infrastructure.Skills;

/// <summary>
/// Writes skill markdown under the configured skills root. Paths are generated from validated
/// code/version values; clients never provide a storage path.
/// </summary>
public sealed class SkillStore(
    IOptions<SkillContentOptions> options,
    ILogger<SkillStore> logger) : ISkillStore
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.RootPath);

    public async Task<StoredSkillContent> SaveAsync(
        Guid? projectId,
        string code,
        string version,
        string content,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Skills:RootPath is not configured.");
        }

        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length > options.Value.MaxBytes)
        {
            throw new InvalidOperationException(
                $"content exceeds the {options.Value.MaxBytes} byte limit.");
        }

        var root = Path.GetFullPath(options.Value.RootPath!)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var scope = projectId?.ToString("N") ?? "_global";
        var directory = Path.Combine(root, scope, code);
        Directory.CreateDirectory(directory);

        var fullPath = Path.Combine(directory, $"{version}.md");

        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid skill path.");
        }

        var temporaryPath = fullPath + ".uploading";

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup.
            }

            throw;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var relativePath = Path.GetRelativePath(root, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');

        logger.LogInformation(
            "Stored skill {Code}@{Version} ({Bytes} bytes) for project {ProjectId}.",
            code,
            version,
            bytes.Length,
            projectId);

        return new StoredSkillContent(relativePath, hash);
    }

    public async Task<string?> ReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var root = Path.GetFullPath(options.Value.RootPath!)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsConfigured)
            {
                return Task.CompletedTask;
            }

            var root = Path.GetFullPath(options.Value.RootPath!)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not delete skill file {Path}.", relativePath);
        }

        return Task.CompletedTask;
    }
}
