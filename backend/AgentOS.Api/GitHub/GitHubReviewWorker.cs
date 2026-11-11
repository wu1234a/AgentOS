using System.Text;
using AgentOS.Application.Reviews;
using AgentOS.Domain.GitHub;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.GitHub;

public sealed class GitHubReviewWorker(
    IServiceScopeFactory scopes,
    IGitHubPullRequestClient client,
    IOptions<GitHubOptions> options,
    ILogger<GitHubReviewWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value;
        if (!configuration.ReviewEnabled || string.IsNullOrWhiteSpace(configuration.ReviewToken))
        {
            logger.LogInformation("GitHub PR review worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AgentOSDbContext>();
                var agent = scope.ServiceProvider.GetRequiredService<IReviewAgent>();
                var now = DateTime.UtcNow;
                var jobs = await dbContext.PullRequestReviewJobs
                    .Where(job => job.Status == "Pending"
                        && (job.NextAttemptAtUtc == null || job.NextAttemptAtUtc <= now))
                    .OrderBy(job => job.CreatedAtUtc)
                    .Take(3)
                    .ToListAsync(stoppingToken);

                foreach (var job in jobs)
                {
                    var claimed = await dbContext.PullRequestReviewJobs
                        .Where(candidate => candidate.Id == job.Id && candidate.Status == "Pending")
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(candidate => candidate.Status, "Running")
                            .SetProperty(candidate => candidate.UpdatedAtUtc, DateTime.UtcNow), stoppingToken);
                    if (claimed == 0)
                    {
                        continue;
                    }

                    await dbContext.Entry(job).ReloadAsync(stoppingToken);

                    try
                    {
                        var metadata = await client.GetMetadataAsync(job.Repository, job.PullRequestNumber, stoppingToken);
                        if (metadata is null)
                        {
                            throw new InvalidOperationException("PR metadata is unavailable.");
                        }

                        if (metadata.State != "open"
                            || !string.Equals(metadata.HeadSha, job.HeadSha, StringComparison.OrdinalIgnoreCase))
                        {
                            job.Status = "Superseded";
                        }
                        else
                        {
                            var diff = await client.GetDiffAsync(job.Repository, job.PullRequestNumber, stoppingToken);
                            if (string.IsNullOrWhiteSpace(diff))
                            {
                                throw new InvalidOperationException("PR diff is unavailable.");
                            }

                            var beforePublish = await client.GetMetadataAsync(job.Repository, job.PullRequestNumber, stoppingToken);
                            if (beforePublish is null
                                || !string.Equals(beforePublish.HeadSha, job.HeadSha, StringComparison.OrdinalIgnoreCase))
                            {
                                job.Status = "Superseded";
                                job.UpdatedAtUtc = DateTime.UtcNow;
                                await dbContext.SaveChangesAsync(stoppingToken);
                                continue;
                            }

                            var review = await agent.ReviewAsync(
                                new ReviewAgentRequest(diff, [], null, job.HeadSha),
                                stoppingToken);

                            if (!review.Success)
                            {
                                throw new InvalidOperationException(review.ErrorMessage ?? "Review failed.");
                            }

                            var body = BuildBody(job, review);
                            var reviewId = await client.CreateReviewAsync(
                                job.Repository,
                                job.PullRequestNumber,
                                job.HeadSha,
                                body,
                                stoppingToken);
                            if (reviewId is null)
                            {
                                // Some tokens cannot publish reviews (for example due to content
                                // rate limits). Keep the gate local rather than retrying forever;
                                // try a regular PR comment as a best-effort fallback.
                                var commented = await client.CreateCommentAsync(
                                    job.Repository,
                                    job.PullRequestNumber,
                                    body,
                                    stoppingToken);
                                logger.LogWarning(
                                    "Could not publish review for PR #{Number}; fallback comment posted: {Commented}.",
                                    job.PullRequestNumber,
                                    commented);
                            }

                            var hasBlockingFinding = review.Findings.Any(finding =>
                                string.Equals(finding.Severity, "Critical", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(finding.Severity, "Major", StringComparison.OrdinalIgnoreCase));
                            job.Status = hasBlockingFinding ? "Blocked" : "Succeeded";
                            job.GitHubReviewId = reviewId;
                            if (hasBlockingFinding)
                            {
                                var message = $"Review has blocking findings. {review.Summary}".Trim();
                                job.LastError = message.Length <= 1000 ? message : message[..1000];
                            }
                            else
                            {
                                job.LastError = null;
                            }
                        }

                        job.UpdatedAtUtc = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                    catch (Exception exception)
                    {
                        job.AttemptCount++;
                        job.LastError = exception.Message.Length <= 1000 ? exception.Message : exception.Message[..1000];
                        job.Status = job.AttemptCount >= configuration.MaxReviewAttempts ? "Failed" : "Pending";
                        job.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, job.AttemptCount)));
                        job.UpdatedAtUtc = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync(stoppingToken);
                        logger.LogWarning(
                            exception,
                            "PR review job {JobId} failed (attempt {Attempt}).",
                            job.Id,
                            job.AttemptCount);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "PR review worker iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private static string BuildBody(PullRequestReviewJob job, ReviewAgentResult review)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"## AgentOS 独立代码审查（{job.Repository} #{job.PullRequestNumber}）");
        builder.AppendLine();
        builder.AppendLine($"head: `{job.HeadSha}`");
        builder.AppendLine();
        builder.AppendLine(review.Summary ?? "未提供摘要。");
        builder.AppendLine();

        foreach (var finding in review.Findings)
        {
            builder.AppendLine($"- **{finding.Severity}** `{finding.FilePath}`{(finding.StartLine is int line ? $":{line}" : string.Empty)}");
            builder.AppendLine($"  - {finding.Reason}");
            builder.AppendLine($"  - 建议：{finding.Suggestion}");
        }

        builder.AppendLine();
        builder.AppendLine("> 历史决策/审查仅供参考，不构成审批或合并依据。");
        return builder.ToString();
    }
}
