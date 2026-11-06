using AgentOS.Application.Runners;
using AgentOS.Domain.Security;
using AgentOS.Domain.Workers;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Runners;

/// <summary>EF Core implementation of runner registration.</summary>
public sealed class RunnerRegistrationStore(AgentOSDbContext dbContext) : IRunnerRegistrationStore
{
    public async Task<Guid> UpsertWorkerAsync(
        RunnerRegistrationRequest request,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var worker = await dbContext.Workers
            .FirstOrDefaultAsync(candidate => candidate.WorkerCode == request.WorkerCode, cancellationToken);

        if (worker is null)
        {
            worker = new Worker
            {
                Id = Guid.CreateVersion7(),
                WorkerCode = request.WorkerCode,
                RegisteredAtUtc = nowUtc
            };

            dbContext.Workers.Add(worker);
        }

        worker.DisplayName = request.DisplayName;
        worker.HostName = request.HostName;
        worker.Platform = request.Platform;
        worker.Architecture = request.Architecture;
        worker.Version = request.Version;
        worker.CapabilitiesJson = request.CapabilitiesJson;
        worker.Status = WorkerStatus.Online;
        worker.LastHeartbeatAtUtc = nowUtc;
        worker.UpdatedAtUtc = nowUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
        return worker.Id;
    }

    public async Task ReplaceRunnerClientAsync(
        Guid workerId,
        ApiClient client,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Re-registration rotates the secret of the stable runner client instead of creating a
        // second row with the same unique ClientCode.
        var existing = await dbContext.ApiClients
            .FirstOrDefaultAsync(candidate => candidate.ClientCode == client.ClientCode, cancellationToken);

        if (existing is not null)
        {
            existing.SecretHash = client.SecretHash;
            existing.ScopesJson = client.ScopesJson;
            existing.IsEnabled = true;
            existing.UpdatedAtUtc = nowUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var previous = await dbContext.ApiClients
            .Where(candidate => candidate.WorkerId == workerId
                                && candidate.ClientType == ApiClientType.Runner
                                && candidate.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var oldClient in previous)
        {
            oldClient.IsEnabled = false;
            oldClient.UpdatedAtUtc = nowUtc;
        }

        dbContext.ApiClients.Add(client);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
