using AgentOS.Domain.Agents;
using AgentOS.Domain.Events;
using AgentOS.Domain.ModelProfiles;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Rules;
using AgentOS.Domain.Skills;
using AgentOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Persistence;

/// <summary>
/// Application database context. AOS-002 introduced Projects and Events, AOS-003 the
/// deduplication columns, and AOS-004 the rule engine schema (agents, model profiles,
/// skills, rules, tasks).
/// </summary>
public sealed class AgentOSDbContext(DbContextOptions<AgentOSDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<ModelProfile> ModelProfiles => Set<ModelProfile>();

    public DbSet<Skill> Skills => Set<Skill>();

    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();

    public DbSet<AutomationTask> Tasks => Set<AutomationTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgentOSDbContext).Assembly);
    }
}
