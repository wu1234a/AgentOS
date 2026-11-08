using AgentOS.Application.CodingRuns;
using AgentOS.Application.Reviews;
using AgentOS.Domain.CodingRuns;
using AgentOS.Domain.Planning;
using AgentOS.Domain.Reviews;
using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentOS.Api.Tests;

public sealed class ReviewServiceTests
{
    [Fact]
    public async Task Successful_review_persists_findings_and_is_idempotent()
    {
        var run = new CodingRun { Id = Guid.NewGuid(), Status = CodingRunStatus.Succeeded, TaskId = Guid.NewGuid() };
        var codingRuns = new FakeCodingRunRepository(run);
        var repository = new FakeReviewRepository();
        var agent = new FakeReviewAgent
        {
            Result = new ReviewAgentResult(
                true,
                "one issue",
                "IssuesFound",
                [new ReviewAgentFinding("Major", "src/a.cs", 3, 4, "bad", "fix")],
                null,
                null)
        };
        var service = CreateService(codingRuns, repository, agent);

        var first = await service.CreateOrRetryAsync(run.Id, CancellationToken.None);
        var second = await service.CreateOrRetryAsync(run.Id, CancellationToken.None);

        Assert.Equal(ReviewOutcome.Ok, first.Outcome);
        Assert.Equal("Succeeded", first.Review!.Status);
        Assert.Single(first.Review.Findings);
        Assert.Equal(1, agent.Calls);
        Assert.Single(repository.Reviews);
        Assert.Equal("Succeeded", second.Review!.Status);
    }

    [Fact]
    public async Task Review_requires_a_succeeded_run()
    {
        var run = new CodingRun { Id = Guid.NewGuid(), Status = CodingRunStatus.Running, TaskId = Guid.NewGuid() };
        var service = CreateService(new FakeCodingRunRepository(run), new FakeReviewRepository(), new FakeReviewAgent());

        var result = await service.CreateOrRetryAsync(run.Id, CancellationToken.None);

        Assert.Equal(ReviewOutcome.Conflict, result.Outcome);
        Assert.Equal("coding-run-not-succeeded", result.ErrorCode);
    }

    [Fact]
    public async Task Failed_review_can_be_retried()
    {
        var run = new CodingRun { Id = Guid.NewGuid(), Status = CodingRunStatus.Succeeded, TaskId = Guid.NewGuid() };
        var repository = new FakeReviewRepository();
        var agent = new FakeReviewAgent
        {
            Result = new ReviewAgentResult(false, null, null, [], "review-agent-error", "failed")
        };
        var service = CreateService(new FakeCodingRunRepository(run), repository, agent);

        var failed = await service.CreateOrRetryAsync(run.Id, CancellationToken.None);
        Assert.Equal("Failed", failed.Review!.Status);

        agent.Result = new ReviewAgentResult(true, "ok", "NoIssues", [], null, null);
        var retried = await service.CreateOrRetryAsync(run.Id, CancellationToken.None);

        Assert.Equal("Succeeded", retried.Review!.Status);
        Assert.Equal(2, retried.Review.AttemptCount);
        Assert.Equal(2, agent.Calls);
    }

    private static ReviewService CreateService(
        ICodingRunRepository codingRuns,
        IReviewRepository repository,
        IReviewAgent agent) => new(
        codingRuns,
        repository,
        agent,
        TimeProvider.System,
        NullLogger<ReviewService>.Instance);

    private sealed class FakeCodingRunRepository(CodingRun run) : ICodingRunRepository
    {
        public Task<CodingRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
            => Task.FromResult<CodingRun?>(run.Id == runId ? run : null);

        public Task<PlanningDraft?> GetDraftAsync(Guid draftId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> ProjectEnabledAsync(Guid projectId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> AgentEnabledAsync(Guid agentId, Guid projectId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CodingRun?> GetRunByDraftAsync(Guid draftId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AutomationTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CodingRun>> ListRunsForSyncAsync(int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<(IReadOnlyList<CodingRunView> Items, int Total)> ListAsync(
            string? status,
            Guid? projectId,
            int limit,
            int offset,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddRunAsync(CodingRun run, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddTaskAsync(AutomationTask task, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddEventAsync(CodingRunEvent evt, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CodingRunEventView>> ListEventsAsync(
            Guid runId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SaveAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeReviewRepository : IReviewRepository
    {
        public List<ReviewRun> Reviews { get; } = [];

        public List<ReviewFinding> Findings { get; } = [];

        public Task<ReviewRun?> GetTrackedAsync(Guid codingRunId, CancellationToken cancellationToken)
            => Task.FromResult(Reviews.FirstOrDefault(review => review.CodingRunId == codingRunId));

        public Task<ReviewView?> GetViewAsync(Guid codingRunId, CancellationToken cancellationToken)
        {
            var review = Reviews.FirstOrDefault(candidate => candidate.CodingRunId == codingRunId);
            if (review is null)
            {
                return Task.FromResult<ReviewView?>(null);
            }

            return Task.FromResult<ReviewView?>(new ReviewView(
                review.Id,
                review.CodingRunId,
                review.BaseRevision,
                review.HeadRevision,
                review.Status.ToString(),
                review.Conclusion?.ToString(),
                review.Summary,
                review.ErrorCode,
                review.ErrorMessage,
                review.AttemptCount,
                review.CreatedAtUtc,
                review.StartedAtUtc,
                review.CompletedAtUtc,
                Findings
                    .Where(finding => finding.ReviewRunId == review.Id)
                    .OrderBy(finding => finding.SortOrder)
                    .Select(finding => new ReviewFindingView(
                        finding.Severity.ToString(),
                        finding.FilePath,
                        finding.StartLine,
                        finding.EndLine,
                        finding.Reason,
                        finding.Suggestion))
                    .ToList()));
        }

        public Task<ReviewChangeSet?> GetChangeSetAsync(Guid taskId, CancellationToken cancellationToken)
            => Task.FromResult<ReviewChangeSet?>(new ReviewChangeSet(
                "--- a/src/a.cs\n+++ b/src/a.cs",
                ["src/a.cs"],
                "abc1234",
                "def5678"));

        public Task AddReviewAsync(ReviewRun review, CancellationToken cancellationToken)
        {
            Reviews.Add(review);
            return Task.CompletedTask;
        }

        public Task AddFindingsAsync(
            IReadOnlyList<ReviewFinding> findings,
            CancellationToken cancellationToken)
        {
            Findings.AddRange(findings);
            return Task.CompletedTask;
        }

        public Task DeleteFindingsAsync(Guid reviewId, CancellationToken cancellationToken)
        {
            Findings.RemoveAll(finding => finding.ReviewRunId == reviewId);
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeReviewAgent : IReviewAgent
    {
        public int Calls { get; private set; }

        public ReviewAgentResult Result { get; set; } = new(true, "ok", "NoIssues", [], null, null);

        public Task<ReviewAgentResult> ReviewAsync(
            ReviewAgentRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }
}
