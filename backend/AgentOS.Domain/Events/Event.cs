using AgentOS.Domain.Projects;

namespace AgentOS.Domain.Events;

/// <summary>
/// A normalized event in the unified event ledger. AOS-002 stores the envelope fields
/// and the validated payload. AOS-003 adds <see cref="PayloadHash"/> and the
/// <c>Source + DedupeKey</c> unique index that make ingestion idempotent; routing is
/// handled by later tasks.
/// </summary>
public sealed class Event
{
    public Guid Id { get; init; }

    public Guid? ProjectId { get; init; }

    public Project? Project { get; init; }

    public string Source { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public string SchemaVersion { get; init; } = string.Empty;

    public string? ExternalEventId { get; init; }

    public string DedupeKey { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string? ResourceUrl { get; init; }

    public string PayloadJson { get; init; } = "{}";

    /// <summary>
    /// SHA-256 (lowercase hex) of the canonical JSON representation of
    /// <see cref="PayloadJson"/>. Null only for rows written before AOS-003; those rows
    /// are hashed lazily the first time a duplicate hits them.
    /// </summary>
    public string? PayloadHash { get; init; }

    public string? SubjectType { get; init; }

    public string? SubjectId { get; init; }

    public string Priority { get; init; } = "normal";

    public EventStatus Status { get; set; } = EventStatus.Received;

    public DateTime? ProcessedAtUtc { get; set; }

    public DateTime OccurredAtUtc { get; init; }

    public DateTime ReceivedAtUtc { get; init; }

    public string? CorrelationId { get; init; }
}
