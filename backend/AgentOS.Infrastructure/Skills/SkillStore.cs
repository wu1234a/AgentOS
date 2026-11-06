using System.Security.Cryptography;
using System.Text;
using AgentOS.Application.Skills;
using AgentOS.Application.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOS.Infrastructure.Skills;

/// <summary>
/// Writes skill markdown under the configured skills root. Paths are generated from validated
/// code/version values and a per-request suffix, so concurrent creations never overwrite or
/// delete each other's files. Reparse points are rejected before any access.
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

        var root = GetRoot();
        var scope = projectId?.ToString("N") ?? "_global";
        var codeDirectory = Path.Combine(root, scope, code);

        EnsureNoReparsePoint(root, codeDirectory);
        Directory.CreateDirectory(codeDirectory);
        EnsureNoReparsePoint(root, codeDirectory);

        // The random suffix makes each request own its file. A losing concurrent insert deletes
        // only its own file, never the committed winner's content.
        var fileName = $"{version}-{Guid.NewGuid().ToString("N")[..8]}.md";
        var fullPath = Path.Combine(codeDirectory, fileName);

        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid skill path.");
        }

        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);

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

        var root = GetRoot();
        var fullPath = Resolve(root, relativePath);

        if (fullPath is null || !File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            EnsureNoReparsePoint(root, fullPath);
            return await File.ReadAllTextAsync(fullPath, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Refusing to read skill file through a symbolic link.");
            return null;
        }
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsConfigured)
            {
                return Task.CompletedTask;
            }

            var root = GetRoot();
            var fullPath = Resolve(root, relativePath);

            if (fullPath is not null && File.Exists(fullPath))
            {
                EnsureNoReparsePoint(root, fullPath);
                File.Delete(fullPath);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not delete skill file {Path}.", relativePath);
        }

        return Task.CompletedTask;
    }

    private string GetRoot()
        => Path.GetFullPath(options.Value.RootPath!)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? Resolve(string root, string relativePath)
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? fullPath
            : null;
    }

    private static void EnsureNoReparsePoint(string root, string target)
    {
        var current = target;
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar);

        while (true)
        {
            FileAttributes? attributes = null;

            if (Directory.Exists(current))
            {
                attributes = new DirectoryInfo(current).Attributes;
            }
            else if (File.Exists(current))
            {
                attributes = new FileInfo(current).Attributes;
            }

            if (attributes?.HasFlag(FileAttributes.ReparsePoint) == true)
            {
                throw new InvalidOperationException("skill path must not contain symbolic links.");
            }

            if (string.Equals(
                    current.TrimEnd(Path.DirectorySeparatorChar),
                    normalizedRoot,
                    StringComparison.Ordinal))
            {
                return;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException("skill path is outside the skills root.");
        }
    }
}
