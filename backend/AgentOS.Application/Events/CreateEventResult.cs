namespace AgentOS.Application.Events;

/// <summary>Outcome of accepting an event: an event id or field-level errors.</summary>
public sealed record CreateEventResult(
    bool IsSuccess,
    Guid EventId,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static CreateEventResult Accepted(Guid eventId) => new(true, eventId, new Dictionary<string, string[]>());

    public static CreateEventResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(false, Guid.Empty, errors);
}
