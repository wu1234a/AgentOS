using AgentOS.Application.Events;
using AgentOS.Application.Projects;
using AgentOS.Application.Rules;
using AgentOS.Application.Runners;
using AgentOS.Application.Agents;
using AgentOS.Application.CodingRuns;
using AgentOS.Application.ModelProfiles;
using AgentOS.Application.Observability;
using AgentOS.Application.Planning;
using AgentOS.Application.Qa;
using AgentOS.Application.Reviews;
using AgentOS.Application.Security;
using AgentOS.Application.Skills;
using AgentOS.Application.Tasks;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Projects;
using AgentOS.Infrastructure.Repositories;
using AgentOS.Infrastructure.Rules;
using AgentOS.Infrastructure.Agents;
using AgentOS.Infrastructure.CodingRuns;
using AgentOS.Infrastructure.Artifacts;
using AgentOS.Infrastructure.ModelProfiles;
using AgentOS.Infrastructure.Observability;
using AgentOS.Infrastructure.Planning;
using AgentOS.Infrastructure.Reviews;
using AgentOS.Infrastructure.Qa;
using AgentOS.Infrastructure.Runners;
using AgentOS.Infrastructure.Security;
using AgentOS.Infrastructure.Skills;
using AgentOS.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentOS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentOSInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AgentOS")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:AgentOS is not configured. Set ConnectionStrings__AgentOS or provide it in appsettings.Development.json.");

        services.AddDbContext<AgentOSDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IProjectLookup, ProjectLookup>();
        services.AddScoped<IAutomationRuleRepository, AutomationRuleRepository>();
        services.AddScoped<IRuleReferenceLookup, RuleReferenceLookup>();
        services.AddScoped<ITaskDispatchStore, TaskDispatchStore>();
        services.AddScoped<ITaskClaimer, TaskClaimer>();
        services.AddScoped<ITaskQueueStore, TaskQueueStore>();
        services.AddScoped<ITaskDependencyCoordinator, TaskDependencyCoordinator>();
        services.AddScoped<ISubtaskRepository, SubtaskRepository>();
        services.AddScoped<ITaskLogStore, TaskLogStore>();
        services.AddScoped<IApiClientStore, ApiClientStore>();
        services.AddScoped<IRunnerRegistrationStore, RunnerRegistrationStore>();
        services.AddScoped<ISkillContentReader, SkillContentReader>();
        services.AddScoped<ISkillStore, SkillStore>();
        services.AddScoped<ISkillRepository, SkillRepository>();
        services.AddScoped<IObservabilityQueryStore, ObservabilityQueryStore>();
        services.AddScoped<IArtifactStore, ArtifactStore>();
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IModelProfileRepository, ModelProfileRepository>();
        services.AddScoped<IPlanningDraftRepository, PlanningDraftRepository>();
        services.AddScoped<ICodingRunRepository, CodingRunRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IQaRepository, QaRepository>();
        services.AddScoped<IApiCompletionClient, ApiCompletionClient>();

        // Stateless and thread-safe; shared so payload hashing and duplicate log sampling stay
        // consistent across requests.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<EventPayloadHasher>();
        services.TryAddSingleton<DuplicateEventLogSampler>();
        services.TryAddSingleton<LeaseTokenService>();

        return services;
    }
}
