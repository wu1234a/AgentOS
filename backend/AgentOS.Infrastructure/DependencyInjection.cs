using AgentOS.Application.Events;
using AgentOS.Application.Projects;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Projects;
using AgentOS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}
