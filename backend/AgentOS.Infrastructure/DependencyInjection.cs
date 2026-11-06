using AgentOS.Application.Events;
using AgentOS.Application.Projects;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Projects;
using AgentOS.Infrastructure.Repositories;
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

        // Stateless and thread-safe; shared so payload hashing and duplicate log sampling stay
        // consistent across requests.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<EventPayloadHasher>();
        services.TryAddSingleton<DuplicateEventLogSampler>();

        return services;
    }
}
