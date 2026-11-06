using AgentOS.Application.Runners;
using AgentOS.Domain.Security;
using AgentOS.Domain.Workers;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>Registration store backed by the in-memory worker/client fakes.</summary>
public sealed class FakeRunnerRegistrationStore(
    FakeTaskQueueStore queue,
    FakeApiClientStore clients) : IRunnerRegistrationStore
{
    public Task<Guid> UpsertWorkerAsync(
        RunnerRegistrationRequest request,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var worker = queue.Workers.FirstOrDefault(candidate => candidate.WorkerCode == request.WorkerCode);

        if (worker is null)
        {
            worker = new Worker
            {
                Id = Guid.CreateVersion7(),
                WorkerCode = request.WorkerCode,
                RegisteredAtUtc = nowUtc
            };
            queue.Workers.Add(worker);
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

        return Task.FromResult(worker.Id);
    }

    public Task ReplaceRunnerClientAsync(
        Guid workerId,
        ApiClient client,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var existing = clients.Clients.FirstOrDefault(candidate => candidate.ClientCode == client.ClientCode);

        if (existing is not null)
        {
            existing.SecretHash = client.SecretHash;
            existing.ScopesJson = client.ScopesJson;
            existing.IsEnabled = true;
            existing.UpdatedAtUtc = nowUtc;
            return Task.CompletedTask;
        }

        foreach (var previous in clients.Clients.Where(candidate =>
                     candidate.WorkerId == workerId
                     && candidate.ClientType == ApiClientType.Runner
                     && candidate.IsEnabled))
        {
            previous.IsEnabled = false;
            previous.UpdatedAtUtc = nowUtc;
        }

        clients.Clients.Add(client);
        return Task.CompletedTask;
    }
}
