using AgentOS.Domain.Events;
using AgentOS.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Persistence;

/// <summary>
/// Application database context. AOS-002 introduces the Projects registry and the
/// Events ledger; Tasks, Rules, Workers, and the remaining schema arrive in later tasks.
/// </summary>
public sealed class AgentOSDbContext(DbContextOptions<AgentOSDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgentOSDbContext).Assembly);
    }
}
