namespace AgentOS.Application.Events;

/// <summary>Paged response for the event list endpoint.</summary>
public sealed record EventListPage(
    IReadOnlyList<EventListItem> Items,
    int Page,
    int PageSize,
    int Total);
