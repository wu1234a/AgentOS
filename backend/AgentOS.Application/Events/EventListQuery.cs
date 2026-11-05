namespace AgentOS.Application.Events;

/// <summary>Filters and pagination for the event list endpoint.</summary>
public sealed record EventListQuery(
    int Page,
    int PageSize,
    string? Source,
    string? EventType,
    string? Status,
    string? Project);
