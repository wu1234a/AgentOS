using System.Text.Json;
using AgentOS.Application.Events;

namespace AgentOS.Api.Tests;

public sealed class EventValidatorTests
{
    private readonly CreateEventValidator _validator = new();

    [Fact]
    public void Valid_envelope_has_no_errors()
        => Assert.Empty(_validator.Validate(Valid()));

    [Fact]
    public void Missing_source_is_rejected()
        => Assert.Contains("source", _validator.Validate(Valid(source: null)).Keys);

    [Fact]
    public void Invalid_event_type_is_rejected()
        => Assert.Contains("eventType", _validator.Validate(Valid(eventType: "not-a-dotted-type")).Keys);

    [Fact]
    public void Non_http_resource_url_is_rejected()
        => Assert.Contains("resourceUrl", _validator.Validate(Valid(resourceUrl: "ftp://example.com/file")).Keys);

    [Fact]
    public void Non_object_payload_is_rejected()
        => Assert.Contains("payload", _validator.Validate(Valid(payload: JsonSerializer.Deserialize<JsonElement>("[]"))).Keys);

    [Fact]
    public void Missing_dedupe_key_is_rejected()
        => Assert.Contains("dedupeKey", _validator.Validate(Valid(dedupeKey: null)).Keys);

    [Fact]
    public void Invalid_priority_is_rejected()
        => Assert.Contains("priority", _validator.Validate(Valid(priority: "critical")).Keys);

    [Fact]
    public void Incomplete_subject_is_rejected()
        => Assert.Contains(
            "subject.id",
            _validator.Validate(Valid(subject: new EventSubjectRequest { Type = "development_task" })).Keys);

    [Fact]
    public void OccurredAtUtc_is_required()
        => Assert.Contains("occurredAtUtc", _validator.Validate(Valid(includeOccurredAt: false)).Keys);

    [Fact]
    public void Timestamp_without_timezone_is_rejected()
        => Assert.Contains(
            "occurredAtUtc",
            _validator.Validate(Valid(occurredAtUtc: "2026-11-05T00:00:00")).Keys);

    private static CreateEventRequest Valid(
        string? source = "3derp",
        string? eventType = "development.task.created",
        string? resourceUrl = null,
        JsonElement? payload = null,
        string? dedupeKey = "3derp:DEV-1",
        string? occurredAtUtc = null,
        bool includeOccurredAt = true,
        string? priority = null,
        EventSubjectRequest? subject = null)
        => new()
        {
            SchemaVersion = "1.0",
            Source = source,
            EventType = eventType,
            Project = "3derp",
            Subject = subject ?? new EventSubjectRequest { Type = "development_task", Id = "DEV-1" },
            Summary = "test event",
            ResourceUrl = resourceUrl,
            OccurredAtUtc = includeOccurredAt
                ? occurredAtUtc ?? "2026-11-05T00:00:00Z"
                : null,
            DedupeKey = dedupeKey,
            Priority = priority,
            Payload = payload ?? JsonSerializer.Deserialize<JsonElement>("{}")
        };
}
