using AgentOS.Domain.GitHub;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.GitHub;

public interface IGitHubReviewQueue
{
    Task EnqueueAsync(
        string repository,
        int number,
        string headSha,
        string action,
        string deliveryId,
        CancellationToken cancellationToken);
}

public sealed class GitHubReviewQueue(AgentOSDbContext dbContext, TimeProvider timeProvider) : IGitHubReviewQueue
{
    public async Task EnqueueAsync(
        string repository,
        int number,
        string headSha,
        string action,
        string deliveryId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.PullRequestReviewJobs.AnyAsync(
                job => job.Repository == repository && job.PullRequestNumber == number && job.HeadSha == headSha,
                cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        dbContext.PullRequestReviewJobs.Add(new PullRequestReviewJob
        {
            Id = Guid.CreateVersion7(),
            Repository = repository,
            PullRequestNumber = number,
            HeadSha = headSha,
            Action = action,
            DeliveryId = deliveryId,
            Status = "Pending",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
