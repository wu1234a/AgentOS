using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Persistence;

/// <summary>
/// Application database context. AOS-001 intentionally contains no business entities:
/// Events, Tasks, Rules, and the remaining schema arrive in later tasks. Keeping the
/// model empty lets the initial migration create only the migration history baseline.
/// </summary>
public sealed class AgentOSDbContext(DbContextOptions<AgentOSDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
