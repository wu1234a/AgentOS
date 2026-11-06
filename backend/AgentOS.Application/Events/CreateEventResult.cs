using AgentOS.Domain.Events;

namespace AgentOS.Application.Events;

/// <summary>How <c>POST /api/v1/events</c> resolved the submission.</summary>
public enum CreateEventOutcome
{
    /// <summary>A new event row was created.</summary>
    Created = 0,

    /// <summary>The deduplication key already exists with the same canonical payload.</summary>
    Duplicate = 1,

    /// <summary>The deduplication key already exists with different content.</summary>
    Conflict = 2,

    /// <summary>The envelope failed validation; nothing was written.</summary>
    Invalid = 3
}

/// <summary>Outcome of accepting an event: created, duplicate, conflict, or field-level errors.</summary>
public sealed record CreateEventResult(
    CreateEventOutcome Outcome,
    Guid EventId,
    Guid? ExistingEventId,
    EventStatus? Status,
    int TasksCreated,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static CreateEventResult Accepted(Guid eventId, EventStatus status, int tasksCreated)
        => new(CreateEventOutcome.Created, eventId, null, status, tasksCreated, EmptyErrors);

    public static CreateEventResult Duplicate(Guid existingEventId, EventStatus status, int tasksCreated)
        => new(CreateEventOutcome.Duplicate, existingEventId, existingEventId, status, tasksCreated, EmptyErrors);

    public static CreateEventResult Conflict(Guid attemptedEventId, Guid existingEventId)
        => new(CreateEventOutcome.Conflict, attemptedEventId, existingEventId, null, 0, EmptyErrors);

    public static CreateEventResult Invalid(IReadOnlyDictionary<string, string[]> errors)
        => new(CreateEventOutcome.Invalid, Guid.Empty, null, null, 0, errors);

    private static readonly Dictionary<string, string[]> EmptyErrors = new(StringComparer.Ordinal);
}
