using AgentOS.Domain.Workers;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.Workers;

public sealed record WorkerView(
    Guid Id,
    string WorkerCode,
    string DisplayName,
    string HostName,
    string Platform,
    string Architecture,
    string Version,
    string Status,
    DateTime LastHeartbeatAtUtc,
    DateTime RegisteredAtUtc,
    int RunningTaskCount);

public sealed record WorkerStats(int Total, int Online, int Offline, int Disabled, int RunningTasks);

public sealed class WorkerAdminService(AgentOSDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<WorkerView>> ListAsync(string? status, CancellationToken cancellationToken)
    {
        var workers = await dbContext.Workers.AsNoTracking().OrderBy(worker => worker.WorkerCode).ToListAsync(cancellationToken);
        var freshAfter = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-2);
        if (Enum.TryParse<WorkerStatus>(status, true, out var parsed))
        {
            workers = workers.Where(worker => EffectiveStatus(worker, freshAfter) == parsed).ToList();
        }

        var counts = await dbContext.Tasks.AsNoTracking()
            .Where(task => task.Status == Domain.Tasks.TaskStatus.Running && task.LeaseOwnerWorkerId != null)
            .GroupBy(task => task.LeaseOwnerWorkerId!.Value)
            .Select(group => new { WorkerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.WorkerId, row => row.Count, cancellationToken);

        return workers.Select(worker => new WorkerView(
            worker.Id, worker.WorkerCode, worker.DisplayName, worker.HostName, worker.Platform,
            worker.Architecture, worker.Version, EffectiveStatus(worker, freshAfter).ToString(), worker.LastHeartbeatAtUtc,
            worker.RegisteredAtUtc, counts.GetValueOrDefault(worker.Id))).ToList();
    }

    public async Task<WorkerStats> StatsAsync(CancellationToken cancellationToken)
    {
        var workers = await dbContext.Workers.AsNoTracking().ToListAsync(cancellationToken);
        var freshAfter = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-2);
        var running = await dbContext.Tasks.AsNoTracking()
            .CountAsync(task => task.Status == Domain.Tasks.TaskStatus.Running, cancellationToken);
        var effective = workers.Select(worker => EffectiveStatus(worker, freshAfter)).ToList();
        return new WorkerStats(
            workers.Count,
            effective.Count(status => status == WorkerStatus.Online),
            effective.Count(status => status == WorkerStatus.Offline),
            effective.Count(status => status == WorkerStatus.Disabled),
            running);
    }

    private static WorkerStatus EffectiveStatus(Worker worker, DateTime freshAfter)
        => worker.Status == WorkerStatus.Disabled
            ? WorkerStatus.Disabled
            : worker.LastHeartbeatAtUtc < freshAfter ? WorkerStatus.Offline : WorkerStatus.Online;

    public async Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var worker = await dbContext.Workers.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (worker is null)
        {
            return false;
        }

        worker.Status = enabled ? WorkerStatus.Online : WorkerStatus.Disabled;
        worker.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
