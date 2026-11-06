using AgentOS.Application.Events;
using AgentOS.Domain.Events;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Repositories;

/// <summary>EF Core implementation of the event ledger persistence seam.</summary>
public sealed class EventRepository(AgentOSDbContext dbContext) : IEventRepository
{
    public async Task<EventWriteResult> TryAddAsync(Event entity, CancellationToken cancellationToken)
    {
        dbContext.Events.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return EventWriteResult.Inserted(entity);
        }
        catch (DbUpdateException ex) when (SqlErrorHelper.IsUniqueKeyViolation(ex))
        {
            // The insert lost the race; the winning row is already committed because the
            // unique index lock is released only after that transaction commits.
            dbContext.Entry(entity).State = EntityState.Detached;

            var existing = await FindByDedupeKeyAsync(entity.Source, entity.DedupeKey, cancellationToken);
            if (existing is null)
            {
                // A unique violation without a matching row means the schema and the model
                // disagree; surface it instead of pretending the event is a duplicate.
                throw;
            }

            return EventWriteResult.DuplicateKey(existing);
        }
    }

    public Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Events
            .AsNoTracking()
            .Include(evt => evt.Project)
            .FirstOrDefaultAsync(evt => evt.Id == id, cancellationToken);

    public Task<Event?> FindByDedupeKeyAsync(string source, string dedupeKey, CancellationToken cancellationToken)
        => dbContext.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(
                evt => evt.Source == source && evt.DedupeKey == dedupeKey,
                cancellationToken);

    public async Task<bool> TrySetPayloadHashAsync(
        Guid eventId,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Events
            .FirstOrDefaultAsync(evt => evt.Id == eventId && evt.PayloadHash == null, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        dbContext.Entry(existing).Property(evt => evt.PayloadHash).CurrentValue = payloadHash;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<EventPage> ListAsync(EventListQuery query, CancellationToken cancellationToken)
    {
        var events = dbContext.Events
            .AsNoTracking()
            .Include(evt => evt.Project)
            .AsQueryable();

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

        var total = await events.CountAsync(cancellationToken);

        var skip = (long)(query.Page - 1) * query.PageSize;
        if (skip > int.MaxValue)
        {
            return new EventPage([], total);
        }

        var items = await events
            .OrderByDescending(evt => evt.ReceivedAtUtc)
            .ThenByDescending(evt => evt.Id)
            .Skip((int)skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new EventPage(items, total);
    }
}
