using System.Text.Json;
using AgentOS.Application.CodingRuns;
using AgentOS.Domain.CodingRuns;
using AgentOS.Domain.Reviews;
using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging;

namespace AgentOS.Application.Reviews;

/// <summary>
/// Independent review orchestration. The review is read-only and has no effect on the Coding
/// Run; a successful review only records findings for a human to judge.
/// </summary>
public sealed class ReviewService(
    ICodingRunRepository codingRuns,
    IReviewRepository repository,
    IReviewAgent agent,
    TimeProvider timeProvider,
    ILogger<ReviewService> logger)
{
    public async Task<ReviewResult> GetAsync(Guid codingRunId, CancellationToken cancellationToken)
    {
        var view = await repository.GetViewAsync(codingRunId, cancellationToken);
        return view is null ? ReviewResult.NotFound() : ReviewResult.Ok(view);
    }

    public async Task<ReviewResult> CreateOrRetryAsync(
        Guid codingRunId,
        CancellationToken cancellationToken)
    {
        var run = await codingRuns.GetRunAsync(codingRunId, cancellationToken);
        if (run is null)
        {
            return ReviewResult.NotFound();
        }

        var existing = await repository.GetTrackedAsync(codingRunId, cancellationToken);

        if (existing is { Status: ReviewStatus.Queued or ReviewStatus.Running or ReviewStatus.Succeeded })
        {
            return ReviewResult.Ok((await repository.GetViewAsync(codingRunId, cancellationToken))!);
        }

        if (run.Status != CodingRunStatus.Succeeded)
        {
            return ReviewResult.Conflict("coding-run-not-succeeded");
        }

        if (run.TaskId is not { } taskId)
        {
            return ReviewResult.Conflict("coding-run-task-missing");
        }

        var changeSet = await repository.GetChangeSetAsync(taskId, cancellationToken);
        if (changeSet is null)
        {
            return ReviewResult.Conflict("changeset-missing");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        ReviewRun review;

        if (existing is { Status: ReviewStatus.Failed })
        {
            review = existing;
            review.Status = ReviewStatus.Running;
            review.AttemptCount++;
            review.Conclusion = null;
            review.Summary = null;
            review.ErrorCode = null;
            review.ErrorMessage = null;
            review.StartedAtUtc = now;
            review.CompletedAtUtc = null;
            review.UpdatedAtUtc = now;
            await repository.DeleteFindingsAsync(review.Id, cancellationToken);
        }
        else
        {
            review = new ReviewRun
            {
                Id = Guid.CreateVersion7(),
                CodingRunId = codingRunId,
                BaseRevision = changeSet.BaseRevision,
                HeadRevision = changeSet.HeadRevision,
                Status = ReviewStatus.Running,
                AttemptCount = 1,
                CreatedAtUtc = now,
                StartedAtUtc = now,
                UpdatedAtUtc = now
            };
            await repository.AddReviewAsync(review, cancellationToken);
        }

        await repository.SaveAsync(cancellationToken);

        ReviewAgentResult result;
        try
        {
            result = await agent.ReviewAsync(
                new ReviewAgentRequest(
                    changeSet.Diff,
                    changeSet.Files,
                    changeSet.BaseRevision,
                    changeSet.HeadRevision),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Review agent failed for coding run {RunId}.", codingRunId);
            result = new ReviewAgentResult(
                false,
                null,
                null,
                [],
                "review-agent-error",
                "Review agent failed.");
        }

        var completedAt = timeProvider.GetUtcNow().UtcDateTime;
        review.CompletedAtUtc = completedAt;
        review.UpdatedAtUtc = completedAt;

        if (!result.Success)
        {
            review.Status = ReviewStatus.Failed;
            review.ErrorCode = result.ErrorCode ?? "review-failed";
            review.ErrorMessage = result.ErrorMessage ?? "Review failed.";
            await repository.SaveAsync(cancellationToken);
            return ReviewResult.Ok((await repository.GetViewAsync(codingRunId, cancellationToken))!);
        }

        review.Status = ReviewStatus.Succeeded;
        review.Conclusion = string.Equals(result.Conclusion, "IssuesFound", StringComparison.OrdinalIgnoreCase)
            ? ReviewConclusion.IssuesFound
            : ReviewConclusion.NoIssues;
        review.Summary = Truncate(result.Summary, 4000);
        review.ErrorCode = null;
        review.ErrorMessage = null;

        var findings = result.Findings
            .Take(100)
            .Select((finding, index) => new ReviewFinding
            {
                Id = Guid.CreateVersion7(),
                ReviewRunId = review.Id,
                SortOrder = index,
                Severity = ParseSeverity(finding.Severity),
                FilePath = Truncate(finding.FilePath, 500),
                StartLine = finding.StartLine,
                EndLine = finding.EndLine,
                Reason = Truncate(finding.Reason, 2000),
                Suggestion = Truncate(finding.Suggestion, 2000)
            })
            .ToList();

        await repository.AddFindingsAsync(findings, cancellationToken);
        await repository.SaveAsync(cancellationToken);

        return ReviewResult.Ok((await repository.GetViewAsync(codingRunId, cancellationToken))!);
    }

    private static ReviewSeverity ParseSeverity(string value)
        => Enum.TryParse<ReviewSeverity>(value, ignoreCase: true, out var severity)
            ? severity
            : ReviewSeverity.Info;

    private static string Truncate(string? value, int max)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}
