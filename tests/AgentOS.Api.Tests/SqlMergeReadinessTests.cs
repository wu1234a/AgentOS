using AgentOS.Api.GitHub;
using AgentOS.Domain.Quality;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlMergeReadinessTests
{
    [SkippableFact]
    public async Task Ready_when_pr_and_quality_gate_pass()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var sha = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        context.QualityGateEvaluations.Add(new QualityGateEvaluation
        {
            Id = Guid.CreateVersion7(),
            Repository = "owner/repo",
            HeadSha = sha,
            Status = "success",
            ReasonCode = "ok",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var client = new FakeClient(sha);
        var service = new MergeReadinessService(
            context,
            client,
            new FakeStatusClient(),
            Options.Create(new GitHubOptions
            {
                MergePolicies = new Dictionary<string, MergePolicyRepositoryOptions>
                {
                    ["owner/repo"] = new() { Enabled = true, RequiredApprovals = 1, RequireQualityGate = true }
                }
            }));

        var result = await service.EvaluateAsync("owner/repo", 7, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Ready);
        Assert.All(result.Checks, check => Assert.True(check.Passed, check.Name));
    }

    private sealed class FakeClient(string sha) : IGitHubPullRequestClient
    {
        public Task<PullRequestMetadata?> GetMetadataAsync(string repository, int number, CancellationToken cancellationToken)
            => Task.FromResult<PullRequestMetadata?>(new PullRequestMetadata(sha, "open"));
        public Task<string?> GetDiffAsync(string repository, int number, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<long?> CreateReviewAsync(string repository, int number, string headSha, string body, CancellationToken cancellationToken) => Task.FromResult<long?>(null);
        public Task<bool> CreateCommentAsync(string repository, int number, string body, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<PullRequestMergeInfo?> GetMergeInfoAsync(string repository, int number, CancellationToken cancellationToken)
            => Task.FromResult<PullRequestMergeInfo?>(new PullRequestMergeInfo("open", false, true, "clean", sha, "master"));
        public Task<int?> GetApprovalCountAsync(string repository, int number, CancellationToken cancellationToken) => Task.FromResult<int?>(1);
        public Task<string?> GetCombinedStatusAsync(string repository, string sha, CancellationToken cancellationToken) => Task.FromResult<string?>("success");
        public Task<IReadOnlyList<PullRequestSummary>?> ListOpenPullRequestsAsync(string repository, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PullRequestSummary>?>(
                [new PullRequestSummary(7, "open", false, true, sha, "aos/test", "master")]);
        public Task<PullRequestMergeResult> MergeAsync(string repository, int number, string headSha, string mergeMethod, CancellationToken cancellationToken)
            => Task.FromResult(new PullRequestMergeResult(true, null));
    }

    private sealed class FakeStatusClient : IGitHubCommitStatusClient
    {
        public Task<bool> PostAsync(string repository, string sha, string state, string description, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<GitHubWorkflowRun>?> GetWorkflowRunsAsync(string repository, string sha, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GitHubWorkflowRun>?>(null);
        public Task<(string Status, string? Reason)> GetBranchProtectionAsync(string repository, string branch, CancellationToken cancellationToken) => Task.FromResult(("required", (string?)null));
    }
}
