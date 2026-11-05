using System.Text.Json;

namespace AgentOS.Application.Events;

/// <summary>Full event detail, including the validated payload object.</summary>
public sealed record EventDetail(
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
    string? CorrelationId,
    JsonElement Payload);
