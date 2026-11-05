using AgentOS.Application.Events;
using AgentOS.Domain.Events;

namespace AgentOS.Api.Tests.Fakes;

public sealed class FakeEventRepository : IEventRepository
{
    private readonly List<Event> _events = [];

    public void Seed(params Event[] events) => _events.AddRange(events);

    public Task AddAsync(Event entity, CancellationToken cancellationToken)
    {
        _events.Add(entity);
        return Task.CompletedTask;
    }

    public Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_events.FirstOrDefault(evt => evt.Id == id));

    public Task<EventPage> ListAsync(EventListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<Event> events = _events;

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
}
