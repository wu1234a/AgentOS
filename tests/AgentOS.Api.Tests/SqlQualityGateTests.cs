using AgentOS.Api.GitHub;
using AgentOS.Domain.Quality;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlQualityGateTests
{
    [SkippableFact]
    public async Task Evaluates_required_workflows_in_order()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var options = Options.Create(new GitHubOptions
        {
            QualityGates = new Dictionary<string, QualityGateRepositoryOptions>
            {
                ["owner/repo"] = new()
                {
                    Enabled = true,
                    RequiredWorkflows = ["build", "test"]
                }
            }
        });
        var statuses = new FakeStatusClient();
        var service = new QualityGateService(
            context,
            statuses,
            options,
            TimeProvider.System,
            NullLogger<QualityGateService>.Instance);

        var sha = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await service.EvaluateAsync("owner/repo", sha, "build", 1, "completed", "success", 1, CancellationToken.None);
        Assert.Equal("pending", statuses.Last?.State);

        await service.EvaluateAsync("owner/repo", sha, "test", 2, "completed", "success", 1, CancellationToken.None);
        Assert.Equal("success", statuses.Last?.State);

        await service.EvaluateAsync("owner/repo", sha, "test", 3, "completed", "failure", 2, CancellationToken.None);
        Assert.Equal("failure", statuses.Last?.State);
    }

    private sealed class FakeStatusClient : IGitHubCommitStatusClient
    {
        public (string State, string Description)? Last { get; private set; }

        public Task<bool> PostAsync(string repository, string sha, string state, string description, CancellationToken cancellationToken)
        {
            Last = (state, description);
            return Task.FromResult(true);
        }
    }
}
