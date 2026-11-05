namespace AgentOS.Application.Security;

/// <summary>
/// Authentication seam for machine API clients. AOS-002 ships a clearly marked
/// development implementation; AOS-006 replaces it with per-client credentials.
/// </summary>
public interface IApiClientAuthenticator
{
    ValueTask<ApiClientIdentity?> AuthenticateAsync(string? apiKey, CancellationToken cancellationToken);
}

/// <summary>Identity proven by a valid API client credential.</summary>
public sealed record ApiClientIdentity(string ClientCode, string ClientType);
