using AgentOS.Application.Scheduling;
using AgentOS.Domain.Scheduling;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Scheduling;

public sealed class ScheduledJobRepository(AgentOSDbContext dbContext) : IScheduledJobRepository
{
    public async Task<IReadOnlyList<ScheduledJob>> ListAsync(CancellationToken cancellationToken)
        => await dbContext.ScheduledJobs.AsNoTracking().OrderBy(job => job.Name).ToListAsync(cancellationToken);

    public Task<ScheduledJob?> GetAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.ScheduledJobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ScheduledJob>> LoadDueAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken)
        => await dbContext.ScheduledJobs
            .Where(job => job.Enabled && job.NextRunAtUtc <= nowUtc)
            .OrderBy(job => job.NextRunAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ScheduledJob job, CancellationToken cancellationToken)
        => await dbContext.ScheduledJobs.AddAsync(job, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public void Remove(ScheduledJob job) => dbContext.ScheduledJobs.Remove(job);
}
