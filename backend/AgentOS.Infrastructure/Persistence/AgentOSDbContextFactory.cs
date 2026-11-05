using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AgentOS.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c>. It reads the same configuration sources
/// as the API (appsettings plus environment variables), so the migration target cannot
/// drift from the runtime target.
/// </summary>
public sealed class AgentOSDbContextFactory : IDesignTimeDbContextFactory<AgentOSDbContext>
{
    public AgentOSDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();

        var options = new DbContextOptionsBuilder<AgentOSDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AgentOSDbContext(options);
    }

    private static string ResolveConnectionString()
    {
        var basePaths = new[]
        {
            Directory.GetCurrentDirectory(),
            Path.Combine(Directory.GetCurrentDirectory(), "backend", "AgentOS.Api"),
            AppContext.BaseDirectory
        };

        foreach (var basePath in basePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(basePath))
            {
                continue;
            }

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("AgentOS");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }
        }

        throw new InvalidOperationException(
            "ConnectionStrings:AgentOS was not found. Set the ConnectionStrings__AgentOS environment variable " +
            "or run dotnet ef from the API project directory (backend/AgentOS.Api).");
    }
}
