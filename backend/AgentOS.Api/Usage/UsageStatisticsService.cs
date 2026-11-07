using AgentOS.Api.Usage;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Usage;

public sealed record UsageStatisticsItem(
    Guid ProjectId,
    DateOnly Date,
    string Provider,
    string? Model,
    long CallCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    decimal? EstimatedCost,
    string Currency,
    string PricingStatus);

public sealed record UsageStatisticsResult(
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<UsageStatisticsItem> Items);

public sealed class UsageStatisticsService(
    AgentOSDbContext dbContext,
    IOptions<UsageOptions> options)
{
    public async Task<UsageStatisticsResult> QueryAsync(
        Guid? projectId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var query = from usage in dbContext.UsageRecords.AsNoTracking()
                    join task in dbContext.Tasks.AsNoTracking() on usage.TaskId equals task.Id
                    where usage.CreatedAtUtc >= fromUtc && usage.CreatedAtUtc < toUtc
                    select new { usage, task.ProjectId };

        if (projectId is Guid filter)
        {
            query = query.Where(row => row.ProjectId == filter);
        }

        var rows = await query
            .GroupBy(row => new
            {
                row.ProjectId,
                Date = row.usage.CreatedAtUtc.Date,
                row.usage.Provider,
                row.usage.Model
            })
            .Select(group => new
            {
                group.Key.ProjectId,
                group.Key.Date,
                group.Key.Provider,
                group.Key.Model,
                CallCount = (long)group.Count(),
                InputTokens = group.Sum(row => (long)row.usage.InputTokens),
                OutputTokens = group.Sum(row => (long)row.usage.OutputTokens),
                TotalTokens = group.Sum(row => (long)row.usage.TotalTokens)
            })
            .OrderBy(item => item.Date)
            .ThenBy(item => item.ProjectId)
            .ThenBy(item => item.Provider)
            .ThenBy(item => item.Model)
            .ToListAsync(cancellationToken);

        var items = rows.Select(row =>
        {
            var key = $"{row.Provider}|{row.Model}";
            if (!options.Value.Pricing.TryGetValue(key, out var price) || !price.Enabled)
            {
                return new UsageStatisticsItem(
                    row.ProjectId,
                    DateOnly.FromDateTime(row.Date),
                    row.Provider,
                    row.Model,
                    row.CallCount,
                    row.InputTokens,
                    row.OutputTokens,
                    row.TotalTokens,
                    null,
                    price?.Currency ?? "USD",
                    "MissingPrice");
            }

            var cost = row.InputTokens / 1_000_000m * price.InputPerMillionTokens
                + row.OutputTokens / 1_000_000m * price.OutputPerMillionTokens;
            return new UsageStatisticsItem(
                row.ProjectId,
                DateOnly.FromDateTime(row.Date),
                row.Provider,
                row.Model,
                row.CallCount,
                row.InputTokens,
                row.OutputTokens,
                row.TotalTokens,
                Math.Round(cost, 6),
                price.Currency,
                "Priced");
        }).ToList();

        return new UsageStatisticsResult(fromUtc, toUtc, items);
    }
}
