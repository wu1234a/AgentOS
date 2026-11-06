using AgentOS.Application.Security;
using AgentOS.Domain.Security;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Security;

/// <summary>EF Core implementation of the machine client credential store.</summary>
public sealed class ApiClientStore(AgentOSDbContext dbContext) : IApiClientStore
{
    public Task<ApiClient?> FindByCodeAsync(string clientCode, CancellationToken cancellationToken)
        => dbContext.ApiClients
            .AsNoTracking()
            .FirstOrDefaultAsync(client => client.ClientCode == clientCode, cancellationToken);

    public Task TouchAsync(Guid clientId, DateTime nowUtc, CancellationToken cancellationToken)
        => dbContext.ApiClients
            .Where(client => client.Id == clientId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(client => client.LastUsedAtUtc, nowUtc),
                cancellationToken);
}
