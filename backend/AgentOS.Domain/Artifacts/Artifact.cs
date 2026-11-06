namespace AgentOS.Domain.Artifacts;

/// <summary>A stored file produced by a task attempt. The database stores metadata only; the
/// bytes live under the configured artifact root.</summary>
public sealed class Artifact
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public Guid TaskId { get; init; }

    public Guid AttemptId { get; init; }

    public Guid? WorkerId { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Relative path under the artifact root; never accepted from a client.</summary>
    public string RelativePath { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public string MediaType { get; init; } = "application/octet-stream";

    public DateTime CreatedAtUtc { get; init; }
}
