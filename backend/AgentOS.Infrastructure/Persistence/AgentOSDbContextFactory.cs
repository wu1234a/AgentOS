using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentOS.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c>. It prefers the
/// <c>ConnectionStrings__AgentOS</c> environment variable and falls back to the local
/// development database, so migrations can be created without starting the API.
/// </summary>
public sealed class AgentOSDbContextFactory : IDesignTimeDbContextFactory<AgentOSDbContext>
{
    private const string DefaultConnectionString =
        "Server=lpc:localhost;Database=AgentOS_Dev;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3";

    public AgentOSDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AgentOS")
            ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<AgentOSDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AgentOSDbContext(options);
    }
}
