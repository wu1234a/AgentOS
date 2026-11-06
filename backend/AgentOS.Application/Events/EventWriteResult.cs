using AgentOS.Domain.Events;

namespace AgentOS.Application.Events;

/// <summary>Outcome of an attempted event insert against the deduplication unique index.</summary>
public enum EventWriteOutcome
{
    /// <summary>The row was inserted; the event is new.</summary>
    Inserted = 0,

    /// <summary>The <c>Source + DedupeKey</c> unique index rejected the row.</summary>
    DuplicateKey = 1
}

/// <summary>
/// Result of <see cref="IEventRepository.TryAddAsync"/>: either the new row or the row that
/// already owns the deduplication key.
/// </summary>
public sealed record EventWriteResult(EventWriteOutcome Outcome, Event? Existing)
{
    public static EventWriteResult Inserted(Event entity) => new(EventWriteOutcome.Inserted, entity);

    public static EventWriteResult DuplicateKey(Event existing) => new(EventWriteOutcome.DuplicateKey, existing);
}
