using AgentOS.Api.Filters;
using AgentOS.Api.Usage;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.Endpoints;

public static class UsageEndpoints
{
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/usage/statistics", QueryAsync)
            .AddEndpointFilter<ApiClientAuthenticationFilter>();
        return app;
    }

    private static async Task<IResult> QueryAsync(
        Guid? projectId,
        DateTime? from,
        DateTime? to,
        UsageStatisticsService service,
        AgentOSDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var fromUtc = (from?.ToUniversalTime() ?? now.AddDays(-30));
        var toUtc = (to?.ToUniversalTime() ?? now);
        if (fromUtc >= toUtc || (toUtc - fromUtc).TotalDays > 366)
        {
            return Results.BadRequest(new { error = "invalid-time-range" });
        }

        if (projectId is Guid id && !await dbContext.Projects.AnyAsync(project => project.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        return Results.Ok(await service.QueryAsync(projectId, fromUtc, toUtc, cancellationToken));
    }
}
