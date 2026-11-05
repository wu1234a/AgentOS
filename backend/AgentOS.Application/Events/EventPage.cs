using AgentOS.Domain.Events;

namespace AgentOS.Application.Events;

/// <summary>One page of domain events plus the total matching count.</summary>
public sealed record EventPage(IReadOnlyList<Event> Items, int Total);
