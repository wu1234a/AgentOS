using AgentOS.Application.Events;
using AgentOS.Domain.Events;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Repositories;

/// <summary>EF Core implementation of the event ledger persistence seam.</summary>
public sealed class EventRepository(AgentOSDbContext dbContext) : IEventRepository
{
    public async Task AddAsync(Event entity, CancellationToken cancellationToken)
    {
        dbContext.Events.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Events
            .AsNoTracking()
            .Include(evt => evt.Project)
            .FirstOrDefaultAsync(evt => evt.Id == id, cancellationToken);

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
