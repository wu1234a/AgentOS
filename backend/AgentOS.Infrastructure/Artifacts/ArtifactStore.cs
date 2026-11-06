using AgentOS.Application.Observability;
using AgentOS.Domain.Artifacts;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOS.Infrastructure.Artifacts;

/// <summary>Local, root-confined artifact storage with SHA-256 and size/type limits.</summary>
public sealed class ArtifactStore(
    AgentOSDbContext dbContext,
    IOptions<ArtifactOptions> options,
    TimeProvider timeProvider,
    ILogger<ArtifactStore> logger) : IArtifactStore
{
    public async Task<Artifact> SaveAsync(
        Guid projectId,
        Guid taskId,
        Guid attemptId,
        Guid workerId,
        string fileName,
        string mediaType,
        Stream content,
        CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        var root = configuration.RootPath;

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArtifactValidationException("Artifacts:RootPath is not configured on this server.");
        }

        if (!configuration.AllowedMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArtifactValidationException($"Media type '{mediaType}' is not allowed.");
        }

        var safeName = SanitizeFileName(fileName);
        var rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relativeDirectory = Path.Combine(projectId.ToString("N"), taskId.ToString("N"), attemptId.ToString("N"));
        var directory = Path.Combine(rootFull, relativeDirectory);
        Directory.CreateDirectory(directory);

        var storedName = safeName;
        var counter = 1;

        while (File.Exists(Path.Combine(directory, storedName)))
        {
            var stem = Path.GetFileNameWithoutExtension(safeName);
            var extension = Path.GetExtension(safeName);
            storedName = $"{stem}-{counter++}{extension}";
        }

        var fullPath = Path.Combine(directory, storedName);
        var temporaryPath = fullPath + ".uploading";
        long sizeBytes = 0;

        try
        {
            string sha256;

            await using (var target = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
                    System.Security.Cryptography.HashAlgorithmName.SHA256);
                var buffer = new byte[81920];

                while (true)
                {
                    var read = await content.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    sizeBytes += read;
                    if (sizeBytes > configuration.MaxBytes)
                    {
                        throw new ArtifactValidationException(
                            $"Artifact exceeds the {configuration.MaxBytes} byte limit.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await target.FlushAsync(cancellationToken);
                sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            // The upload stream must be closed before the file can be moved on Windows.
            File.Move(temporaryPath, fullPath, overwrite: false);

            var artifact = new Artifact
            {
                Id = Guid.CreateVersion7(),
                ProjectId = projectId,
                TaskId = taskId,
                AttemptId = attemptId,
                WorkerId = workerId,
                Name = safeName,
                RelativePath = Path.GetRelativePath(rootFull, fullPath)
                    .Replace(Path.DirectorySeparatorChar, '/'),
                Sha256 = sha256,
                SizeBytes = sizeBytes,
                MediaType = mediaType,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };

            dbContext.Artifacts.Add(artifact);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Stored artifact {ArtifactId} ({Name}, {Size} bytes) for task {TaskId}.",
                artifact.Id,
                artifact.Name,
                artifact.SizeBytes,
                taskId);

            return artifact;
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public async Task<(Artifact Artifact, Stream Stream)?> OpenAsync(
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        var artifact = await dbContext.Artifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == artifactId, cancellationToken);

        if (artifact is null || string.IsNullOrWhiteSpace(options.Value.RootPath))
        {
            return null;
        }

        var root = Path.GetFullPath(options.Value.RootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            return null;
        }

        return (artifact, new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    public async Task<IReadOnlyList<ArtifactView>> ListAsync(Guid taskId, CancellationToken cancellationToken)
        => await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.TaskId == taskId)
            .OrderBy(artifact => artifact.CreatedAtUtc)
            .Select(artifact => new ArtifactView(
                artifact.Id,
                artifact.Name,
                artifact.MediaType,
                artifact.SizeBytes,
                artifact.Sha256,
                artifact.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "artifact";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Length <= 120 ? name : name[..120];
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup of a failed upload.
        }
    }
}
