namespace AgentOS.Domain.Workers;

/// <summary>Lifecycle status of a registered worker (local Runner).</summary>
public enum WorkerStatus
{
    Online = 0,
    Offline = 1,
    Disabled = 2
}

/// <summary>
/// A registered Runner. AOS-005 only requires identity and enablement for lease ownership;
/// registration, heartbeat and capability negotiation are completed by AOS-006.
/// </summary>
public sealed class Worker
{
    public Guid Id { get; init; }

    /// <summary>Stable, unique, human-readable identifier supplied at registration.</summary>
    public string WorkerCode { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string HostName { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string Architecture { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string CapabilitiesJson { get; set; } = "{}";

    public WorkerStatus Status { get; set; } = WorkerStatus.Online;

    public DateTime LastHeartbeatAtUtc { get; set; }

    public DateTime RegisteredAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
