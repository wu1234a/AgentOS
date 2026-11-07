using System.Text.Json;
using AgentOS.Application.Events;

namespace AgentOS.Api.Business;

public sealed class BusinessEventRequest
{
    public string? Source { get; init; }
    public string? EventType { get; init; }
    public string? Project { get; init; }
    public string? DedupeKey { get; init; }
    public string? Summary { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public JsonElement? Payload { get; init; }
    public DateTime? OccurredAtUtc { get; init; }
}

public sealed class BusinessEventService(EventService events)
{
    private static readonly HashSet<string> Sources = new(StringComparer.OrdinalIgnoreCase) { "3derp", "emperor" };

    public async Task<(CreateEventResult? Result, string? Error)> SubmitAsync(
        BusinessEventRequest request,
        CancellationToken cancellationToken)
    {
        var source = request.Source?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Sources.Contains(source))
        {
            return (null, "source-not-allowed");
        }

        var eventType = request.EventType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (eventType.Length is < 1 or > 80 || !eventType.All(character =>
                char.IsLetterOrDigit(character) || character is '.' or '-' or '_'))
        {
            return (null, "invalid-event-type");
        }

        var dedupeKey = request.DedupeKey?.Trim() ?? string.Empty;
        if (dedupeKey.Length is < 1 or > 200)
        {
            return (null, "invalid-dedupe-key");
        }

        var project = request.Project?.Trim() ?? string.Empty;
        if (project.Length == 0)
        {
            return (null, "project-required");
        }

        if (request.OccurredAtUtc is DateTime occurred && occurred.Kind == DateTimeKind.Unspecified)
        {
            return (null, "occurred-at-requires-timezone");
        }

        var occurredAt = request.OccurredAtUtc is DateTime value
            ? value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value
            : DateTime.UtcNow;
        var subject = !string.IsNullOrWhiteSpace(request.SubjectType) && !string.IsNullOrWhiteSpace(request.SubjectId)
            ? new EventSubjectRequest { Type = request.SubjectType, Id = request.SubjectId }
            : null;
        var result = await events.CreateAsync(new CreateEventRequest
        {
            SchemaVersion = "1.0",
            Source = source,
            EventType = $"{source}.{eventType}",
            ExternalEventId = dedupeKey,
            DedupeKey = dedupeKey,
            Project = project,
            Subject = subject,
            Summary = string.IsNullOrWhiteSpace(request.Summary) ? $"{source}.{eventType}" : request.Summary,
            OccurredAtUtc = occurredAt.ToString("O"),
            Priority = "normal",
            Payload = request.Payload ?? System.Text.Json.JsonSerializer.SerializeToElement(new { })
        }, cancellationToken);
        return (result, null);
    }
}
