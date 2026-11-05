using AgentOS.Domain.Events;

namespace AgentOS.Application.Events;

/// <summary>Persistence seam for the event ledger.</summary>
public interface IEventRepository
{
    Task AddAsync(Event entity, CancellationToken cancellationToken);

    Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<EventPage> ListAsync(EventListQuery query, CancellationToken cancellationToken);
}
