using AgentOS.Domain.GitHub;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.GitHub;

/// <summary>
/// Reviews and merges AgentOS pull requests without requiring a GitHub webhook. The worker is
/// intentionally conservative: only configured repositories, AgentOS branch prefixes, exact
/// reviewed head SHAs, and mergeable non-draft PRs are eligible.
/// </summary>
public sealed class GitHubAutoMergeWorker(
    IServiceScopeFactory scopes,
    IGitHubPullRequestClient client,
    IOptions<GitHubOptions> options,
    ILogger<GitHubAutoMergeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value;
        if (configuration.AutoMerges.Count == 0)
        {
            logger.LogInformation("GitHub auto-merge worker is disabled.");
            return;
        }

        if (!configuration.ReviewEnabled || string.IsNullOrWhiteSpace(configuration.ReviewToken))
        {
            logger.LogWarning("GitHub auto-merge needs ReviewEnabled and ReviewToken; worker is idle.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var minimumDelay = 30;
            foreach (var (repository, policy) in configuration.AutoMerges)
            {
                if (!policy.Enabled)
                {
                    continue;
                }

                minimumDelay = Math.Min(minimumDelay, Math.Clamp(policy.PollSeconds, 10, 300));
                try
                {
                    await ProcessRepositoryAsync(repository, policy, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "GitHub auto-merge iteration failed for {Repository}.", repository);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(minimumDelay), stoppingToken);
        }
    }

    private async Task ProcessRepositoryAsync(
        string repository,
        AutoMergeRepositoryOptions policy,
        CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgentOSDbContext>();
        var reviewQueue = scope.ServiceProvider.GetRequiredService<IGitHubReviewQueue>();

        var pullRequests = await client.ListOpenPullRequestsAsync(repository, cancellationToken);
        if (pullRequests is null)
        {
            logger.LogWarning("Could not list open pull requests for {Repository}.", repository);
            return;
        }

        foreach (var pullRequest in pullRequests)
        {
            if (!string.Equals(pullRequest.State, "open", StringComparison.OrdinalIgnoreCase)
                || pullRequest.Draft
                || !string.Equals(pullRequest.BaseRef, policy.BaseBranch, StringComparison.OrdinalIgnoreCase)
                || !pullRequest.HeadRef.StartsWith(policy.BranchPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var review = await dbContext.PullRequestReviewJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    job => job.Repository == repository
                        && job.PullRequestNumber == pullRequest.Number
                        && job.HeadSha == pullRequest.HeadSha,
                    cancellationToken);

            if (review is null)
            {
                await reviewQueue.EnqueueAsync(
                    repository,
                    pullRequest.Number,
                    pullRequest.HeadSha,
                    "auto-merge",
                    $"auto-merge:{repository}:{pullRequest.Number}:{pullRequest.HeadSha}",
                    cancellationToken);
                logger.LogInformation(
                    "Queued review for {Repository} PR #{Number} at {Sha}.",
                    repository,
                    pullRequest.Number,
                    pullRequest.HeadSha);
                continue;
            }

            if (review.Status is "Pending" or "Running")
            {
                continue;
            }

            if (policy.RequireReview && review.Status != "Succeeded")
            {
                logger.LogInformation(
                    "Skipping auto-merge for {Repository} PR #{Number}: review is {Status}.",
                    repository,
                    pullRequest.Number,
                    review.Status);
                continue;
            }

            var mergeable = pullRequest.Mergeable;
            if (mergeable is null)
            {
                // GitHub's list endpoint commonly returns mergeable=null; refresh the single PR.
                var info = await client.GetMergeInfoAsync(repository, pullRequest.Number, cancellationToken);
                mergeable = info?.Mergeable;
            }

            if (mergeable != true)
            {
                logger.LogInformation(
                    "Skipping auto-merge for {Repository} PR #{Number}: mergeable={Mergeable}.",
                    repository,
                    pullRequest.Number,
                    mergeable);
                continue;
            }

            var merge = await client.MergeAsync(
                repository,
                pullRequest.Number,
                pullRequest.HeadSha,
                policy.MergeMethod,
                cancellationToken);

            if (merge.Success)
            {
                logger.LogInformation(
                    "Auto-merged {Repository} PR #{Number} at {Sha}.",
                    repository,
                    pullRequest.Number,
                    pullRequest.HeadSha);
            }
            else
            {
                logger.LogWarning(
                    "Auto-merge failed for {Repository} PR #{Number}: {Error}.",
                    repository,
                    pullRequest.Number,
                    merge.Error);
            }
        }
    }
}
