using AgentOS.Application.Security;

namespace AgentOS.Api.Security;

/// <summary>
/// Fail-closed authenticator for non-Development environments until AOS-006 adds
/// per-client credentials. No request is accepted anonymously.
/// </summary>
public sealed class UnconfiguredApiClientAuthenticator : IApiClientAuthenticator
{
    public ValueTask<ApiClientIdentity?> AuthenticateAsync(
        string? apiKey,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ApiClientIdentity?>(null);
}
