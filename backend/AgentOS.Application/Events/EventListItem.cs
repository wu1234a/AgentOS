namespace AgentOS.Application.Events;

/// <summary>Event summary returned by the list endpoint. The full payload is omitted.</summary>
public sealed record EventListItem(
    Guid Id,
    Guid? ProjectId,
    string? ProjectSlug,
    string Source,
    string EventType,
    string SchemaVersion,
    string? ExternalEventId,
    string Summary,
    string? ResourceUrl,
    string Status,
    DateTime? ProcessedAtUtc,
    string? SubjectType,
    string? SubjectId,
    string Priority,
    DateTime OccurredAtUtc,
    DateTime ReceivedAtUtc,
    string? CorrelationId);
