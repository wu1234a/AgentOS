using AgentOS.Domain.Agents;
using AgentOS.Domain.Approvals;
using AgentOS.Domain.CodingRuns;
using AgentOS.Domain.Decisions;
using AgentOS.Domain.GitHub;
using AgentOS.Domain.Quality;
using AgentOS.Domain.Scheduling;
using AgentOS.Domain.Artifacts;
using AgentOS.Domain.Events;
using AgentOS.Domain.ModelProfiles;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Outbox;
using AgentOS.Domain.Planning;
using AgentOS.Domain.Reviews;
using AgentOS.Domain.Qa;
using AgentOS.Domain.Rules;
using AgentOS.Domain.Security;
using AgentOS.Domain.Skills;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Telegram;
using AgentOS.Domain.Usage;
using AgentOS.Domain.Workers;
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

    public DbSet<Worker> Workers => Set<Worker>();

    public DbSet<TaskAttempt> TaskAttempts => Set<TaskAttempt>();

    public DbSet<TaskLog> TaskLogs => Set<TaskLog>();

    public DbSet<ApiClient> ApiClients => Set<ApiClient>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<PlanningDraft> PlanningDrafts => Set<PlanningDraft>();
    public DbSet<CodingRun> CodingRuns => Set<CodingRun>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<DecisionMemory> DecisionMemories => Set<DecisionMemory>();
    public DbSet<PullRequestReviewJob> PullRequestReviewJobs => Set<PullRequestReviewJob>();
    public DbSet<QualityGateRun> QualityGateRuns => Set<QualityGateRun>();
    public DbSet<QualityGateEvaluation> QualityGateEvaluations => Set<QualityGateEvaluation>();
    public DbSet<ScheduledJob> ScheduledJobs => Set<ScheduledJob>();
    public DbSet<TelegramNotification> TelegramNotifications => Set<TelegramNotification>();
    public DbSet<TelegramPollerState> TelegramPollerState => Set<TelegramPollerState>();
    public DbSet<TelegramProcessedUpdate> TelegramProcessedUpdates => Set<TelegramProcessedUpdate>();
    public DbSet<TelegramRejectSession> TelegramRejectSessions => Set<TelegramRejectSession>();
    public DbSet<CodingRunEvent> CodingRunEvents => Set<CodingRunEvent>();
    public DbSet<ReviewRun> ReviewRuns => Set<ReviewRun>();
    public DbSet<ReviewFinding> ReviewFindings => Set<ReviewFinding>();
    public DbSet<QaRun> QaRuns => Set<QaRun>();
    public DbSet<QaCommandResult> QaCommandResults => Set<QaCommandResult>();
    public DbSet<QaTestCase> QaTestCases => Set<QaTestCase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgentOSDbContext).Assembly);
    }
}
