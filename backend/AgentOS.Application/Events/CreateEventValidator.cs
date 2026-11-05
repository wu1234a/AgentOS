using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentOS.Application.Events;

/// <summary>
/// Validates the standard event envelope. Validation is deliberately manual so the
/// API can return field-level errors without model-binding exceptions.
/// </summary>
public sealed class CreateEventValidator
{
    private static readonly Regex SourcePattern = new(
        "^[a-z0-9][a-z0-9-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EventTypePattern = new(
        "^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SchemaVersionPattern = new(
        "^[0-9]+\\.[0-9]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OffsetPattern = new(
        @"[+-]\d{2}:?\d{2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Priorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "low",
        "normal",
        "high",
        "urgent"
    };

    public IReadOnlyDictionary<string, string[]> Validate(CreateEventRequest request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var messages))
            {
                messages = [];
                errors[field] = messages;
            }

            messages.Add(message);
        }

        if (string.IsNullOrWhiteSpace(request.SchemaVersion))
        {
            Add("schemaVersion", "schemaVersion is required.");
        }
        else if (request.SchemaVersion.Length > 16)
        {
            Add("schemaVersion", "schemaVersion must be 16 characters or fewer.");
        }
        else if (!SchemaVersionPattern.IsMatch(request.SchemaVersion))
        {
            Add("schemaVersion", "schemaVersion must use major.minor format, for example 1.0.");
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            Add("source", "source is required.");
        }
        else if (request.Source.Length > 64)
        {
            Add("source", "source must be 64 characters or fewer.");
        }
        else if (!SourcePattern.IsMatch(request.Source))
        {
            Add("source", "source must be a lowercase slug or kebab-case identifier.");
        }

        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            Add("eventType", "eventType is required.");
        }
        else if (request.EventType.Length > 120)
        {
            Add("eventType", "eventType must be 120 characters or fewer.");
        }
        else if (!EventTypePattern.IsMatch(request.EventType))
        {
            Add("eventType", "eventType must use the domain.entity.action format.");
        }

        if (!string.IsNullOrWhiteSpace(request.ExternalEventId) && request.ExternalEventId.Length > 200)
        {
            Add("externalEventId", "externalEventId must be 200 characters or fewer.");
        }

        if (!string.IsNullOrWhiteSpace(request.Project) && request.Project.Length > 80)
        {
            Add("project", "project must be 80 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(request.Summary))
        {
            Add("summary", "summary is required.");
        }
        else if (request.Summary.Length > 500)
        {
            Add("summary", "summary must be 500 characters or fewer.");
        }

        if (!string.IsNullOrWhiteSpace(request.ResourceUrl))
        {
            if (request.ResourceUrl.Length > 2048)
            {
                Add("resourceUrl", "resourceUrl must be 2048 characters or fewer.");
            }
            else if (!Uri.TryCreate(request.ResourceUrl, UriKind.Absolute, out var resourceUri)
                     || (resourceUri.Scheme != Uri.UriSchemeHttp && resourceUri.Scheme != Uri.UriSchemeHttps))
            {
                Add("resourceUrl", "resourceUrl must be an absolute HTTP or HTTPS URL.");
            }
        }

        if (string.IsNullOrWhiteSpace(request.OccurredAtUtc))
        {
            Add("occurredAtUtc", "occurredAtUtc is required.");
        }
        else if (!TryParseOccurredAtUtc(request.OccurredAtUtc, out _))
        {
            Add("occurredAtUtc", "occurredAtUtc must be a valid ISO 8601 timestamp with an explicit timezone offset or Z.");
        }

        if (string.IsNullOrWhiteSpace(request.DedupeKey))
        {
            Add("dedupeKey", "dedupeKey is required.");
        }
        else if (request.DedupeKey.Length > 400)
        {
            Add("dedupeKey", "dedupeKey must be 400 characters or fewer.");
        }

        if (!string.IsNullOrWhiteSpace(request.Priority) && !Priorities.Contains(request.Priority.Trim()))
        {
            Add("priority", "priority must be one of low, normal, high, or urgent.");
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId) && request.CorrelationId.Length > 100)
        {
            Add("correlationId", "correlationId must be 100 characters or fewer.");
        }

        if (request.Subject is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Subject.Type))
            {
                Add("subject.type", "subject.type is required when subject is provided.");
            }
            else if (request.Subject.Type.Length > 64)
            {
                Add("subject.type", "subject.type must be 64 characters or fewer.");
            }

            if (string.IsNullOrWhiteSpace(request.Subject.Id))
            {
                Add("subject.id", "subject.id is required when subject is provided.");
            }
            else if (request.Subject.Id.Length > 200)
            {
                Add("subject.id", "subject.id must be 200 characters or fewer.");
            }
        }

        var payload = request.Payload;
        if (payload is null || payload.Value.ValueKind == JsonValueKind.Undefined)
        {
            Add("payload", "payload is required.");
        }
        else if (payload.Value.ValueKind != JsonValueKind.Object)
        {
            Add("payload", "payload must be a JSON object.");
        }

        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Parses <c>occurredAtUtc</c> only when the value carries an explicit timezone
    /// (<c>Z</c> or an offset). A bare local timestamp is rejected instead of being
    /// silently interpreted in the server's timezone.
    /// </summary>
    public static bool TryParseOccurredAtUtc(string? value, out DateTimeOffset occurredAtUtc)
    {
        occurredAtUtc = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var hasExplicitOffset = trimmed.EndsWith('Z')
                                || trimmed.EndsWith('z')
                                || OffsetPattern.IsMatch(trimmed);

        if (!hasExplicitOffset)
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out occurredAtUtc);
    }
}
