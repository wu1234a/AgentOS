namespace AgentOS.Domain.Events;

/// <summary>Lifecycle status of an event in the event ledger.</summary>
public enum EventStatus
{
    Received = 0,
    Processed = 1,
    Ignored = 2,
    Rejected = 3,
    Failed = 4
}
