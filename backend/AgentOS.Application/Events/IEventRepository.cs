using AgentOS.Domain.Events;

namespace AgentOS.Application.Events;

/// <summary>Persistence seam for the event ledger.</summary>
public interface IEventRepository
{
    /// <summary>
    /// Inserts the event and lets the database unique index decide whether the deduplication
    /// key is already taken. Callers must not pre-check existence and then insert: the index is
    /// the only reliable guard.
    /// </summary>
    Task<EventWriteResult> TryAddAsync(Event entity, CancellationToken cancellationToken);

    Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Finds the event owning a deduplication key, if any.</summary>
    Task<Event?> FindByDedupeKeyAsync(string source, string dedupeKey, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the payload hash of a legacy row that predates AOS-003. Returns false when the
    /// row already had a hash (a concurrent request won the backfill).
    /// </summary>
    Task<bool> TrySetPayloadHashAsync(Guid eventId, string payloadHash, CancellationToken cancellationToken);

    Task<EventPage> ListAsync(EventListQuery query, CancellationToken cancellationToken);
}
