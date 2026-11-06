namespace AgentOS.Domain.Security;

/// <summary>Kind of machine identity an API client represents.</summary>
public enum ApiClientType
{
    EventSource = 0,
    Runner = 1,
    Service = 2
}

/// <summary>
/// Machine API credential. Only the hash of the secret is stored; the plaintext secret is
/// returned exactly once, at registration/rotation time.
/// </summary>
public sealed class ApiClient
{
    public Guid Id { get; init; }

    public string ClientCode { get; set; } = string.Empty;

    public ApiClientType ClientType { get; init; }

    /// <summary>Project scope; null for clients that may act across projects (for example a Runner).</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Worker bound to this credential (Runners only).</summary>
    public Guid? WorkerId { get; init; }

    /// <summary>SHA-256 (lowercase hex) of the client secret.</summary>
    public string SecretHash { get; set; } = string.Empty;

    /// <summary>JSON array of granted scopes, for example <c>["tasks:claim","tasks:report"]</c>.</summary>
    public string ScopesJson { get; set; } = "[]";

    public bool IsEnabled { get; set; }

    public DateTime? ExpiresAtUtc { get; init; }

    public DateTime? LastUsedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; set; }
}
