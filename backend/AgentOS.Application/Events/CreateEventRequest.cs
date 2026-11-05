using System.Text.Json;

namespace AgentOS.Application.Events;

/// <summary>
/// Standard event envelope accepted by <c>POST /api/v1/events</c>. Optional properties
/// are nullable so the validator can produce field-level errors instead of relying on
/// model-binding exceptions.
/// </summary>
public sealed class CreateEventRequest
{
    public string? SchemaVersion { get; init; }

    public string? Source { get; init; }

    public string? EventType { get; init; }

    public string? ExternalEventId { get; init; }

    public string? Project { get; init; }

    public EventSubjectRequest? Subject { get; init; }

    public string? Summary { get; init; }

    public string? ResourceUrl { get; init; }

    public string? OccurredAtUtc { get; init; }

    public string? DedupeKey { get; init; }

    public string? Priority { get; init; }

    public string? CorrelationId { get; init; }

    public JsonElement? Payload { get; init; }
}

/// <summary>Subject of an event: the business object the event is about.</summary>
public sealed class EventSubjectRequest
{
    public string? Type { get; init; }

    public string? Id { get; init; }
}
