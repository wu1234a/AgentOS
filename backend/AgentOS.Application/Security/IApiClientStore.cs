using AgentOS.Domain.Security;

namespace AgentOS.Application.Security;

/// <summary>Persistence seam for machine API clients.</summary>
public interface IApiClientStore
{
    Task<ApiClient?> FindByCodeAsync(string clientCode, CancellationToken cancellationToken);

    Task TouchAsync(Guid clientId, DateTime nowUtc, CancellationToken cancellationToken);
}

/// <summary>Result of a runner registration request.</summary>
public sealed record RunnerRegistration(
    Guid WorkerId,
    string ClientCode,
    string ClientSecret,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAtUtc);
