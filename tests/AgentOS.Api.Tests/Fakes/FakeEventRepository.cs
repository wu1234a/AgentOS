using AgentOS.Application.Events;
using AgentOS.Domain.Events;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>
/// In-memory event ledger used by endpoint tests. It enforces the same
/// <c>Source + DedupeKey</c> uniqueness rule as the SQL unique index so the API behaviour
/// under duplicates and conflicts can be tested without a database.
/// </summary>
public sealed class FakeEventRepository : IEventRepository
{
    private readonly List<Event> _events = [];
    private readonly Lock _sync = new();

    /// <summary>Number of insert attempts; lets tests prove the duplicate path skips the insert.</summary>
    public int InsertAttempts { get; private set; }

    /// <summary>
    /// Simulates a concurrent submission that wins between the service's read and insert:
    /// the next insert reports this event as the existing owner of the key.
    /// </summary>
    public Event? ConcurrentWinner { get; set; }

    public void Seed(params Event[] events)
    {
        lock (_sync)
        {
            _events.AddRange(events);
        }
    }

    public Task<EventWriteResult> TryAddAsync(Event entity, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            InsertAttempts++;

            if (ConcurrentWinner is not null)
            {
                return Task.FromResult(EventWriteResult.DuplicateKey(ConcurrentWinner));
            }

            var existing = FindLocked(entity.Source, entity.DedupeKey);
            if (existing is not null)
            {
                return Task.FromResult(EventWriteResult.DuplicateKey(existing));
            }

            _events.Add(entity);
            return Task.FromResult(EventWriteResult.Inserted(entity));
        }
    }

    public Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult(_events.FirstOrDefault(evt => evt.Id == id));
        }
    }

    public Task<Event?> FindByDedupeKeyAsync(string source, string dedupeKey, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult(FindLocked(source, dedupeKey));
        }
    }

    public Task<bool> TrySetPayloadHashAsync(Guid eventId, string payloadHash, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            var index = _events.FindIndex(evt => evt.Id == eventId);
            if (index < 0 || _events[index].PayloadHash is not null)
            {
                return Task.FromResult(false);
            }

            _events[index] = CopyWithHash(_events[index], payloadHash);
            return Task.FromResult(true);
        }
    }

    public Task<EventPage> ListAsync(EventListQuery query, CancellationToken cancellationToken)
    {
        List<Event> snapshot;
        lock (_sync)
        {
            snapshot = [.. _events];
        }

        IEnumerable<Event> events = snapshot;

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            events = events.Where(evt => evt.Source == query.Source);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            events = events.Where(evt => evt.EventType == query.EventType);
        }

        if (!string.IsNullOrWhiteSpace(query.Status)
            && Enum.TryParse<EventStatus>(query.Status, ignoreCase: true, out var status))
        {
            events = events.Where(evt => evt.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Project))
        {
            events = events.Where(evt => evt.Project != null && evt.Project.Slug == query.Project);
        }

        var total = events.Count();
        var items = events
            .OrderByDescending(evt => evt.ReceivedAtUtc)
            .ThenByDescending(evt => evt.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult(new EventPage(items, total));
    }

    private Event? FindLocked(string source, string dedupeKey)
        => _events.FirstOrDefault(evt => Matches(evt.Source, source) && Matches(evt.DedupeKey, dedupeKey));

    /// <summary>
    /// Mirrors the SQL Server default collation used by the real unique index: case-insensitive
    /// and trailing-space-insensitive. (A real installation can use a different collation; the
    /// database remains the authority.)
    /// </summary>
    private static bool Matches(string stored, string candidate)
        => string.Equals(stored.TrimEnd(), candidate.TrimEnd(), StringComparison.OrdinalIgnoreCase);

    private static Event CopyWithHash(Event source, string payloadHash) => new()
    {
        Id = source.Id,
        ProjectId = source.ProjectId,
        Project = source.Project,
        Source = source.Source,
        EventType = source.EventType,
        SchemaVersion = source.SchemaVersion,
        ExternalEventId = source.ExternalEventId,
        DedupeKey = source.DedupeKey,
        Summary = source.Summary,
        ResourceUrl = source.ResourceUrl,
        PayloadJson = source.PayloadJson,
        PayloadHash = payloadHash,
        SubjectType = source.SubjectType,
        SubjectId = source.SubjectId,
        Priority = source.Priority,
        Status = source.Status,
        ProcessedAtUtc = source.ProcessedAtUtc,
        OccurredAtUtc = source.OccurredAtUtc,
        ReceivedAtUtc = source.ReceivedAtUtc,
        CorrelationId = source.CorrelationId
    };
}
