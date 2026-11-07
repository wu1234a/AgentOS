using AgentOS.Domain.Quality;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.GitHub;

public sealed class QualityGateService(
    AgentOSDbContext dbContext,
    IGitHubCommitStatusClient statusClient,
    IOptions<GitHubOptions> options,
    TimeProvider timeProvider,
    ILogger<QualityGateService> logger)
{
    public async Task EvaluateAsync(
        string repository,
        string headSha,
        string workflowName,
        long runId,
        string status,
        string? conclusion,
        int runAttempt,
        CancellationToken cancellationToken)
    {
        if (!options.Value.QualityGates.TryGetValue(repository, out var configuration) || !configuration.Enabled)
        {
            return;
        }

        var existing = await dbContext.QualityGateRuns.FirstOrDefaultAsync(
            run => run.Repository == repository && run.HeadSha == headSha && run.WorkflowName == workflowName,
            cancellationToken);

        if (existing is not null && existing.RunId > runId)
        {
            return;
        }

        if (existing is null)
        {
            dbContext.QualityGateRuns.Add(new QualityGateRun
            {
                Id = Guid.CreateVersion7(),
                Repository = repository,
                HeadSha = headSha,
                WorkflowName = workflowName,
                RunId = runId,
                Status = status,
                Conclusion = conclusion,
                RunAttempt = runAttempt,
                UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
        }
        else
        {
            existing.RunId = runId;
            existing.Status = status;
            existing.Conclusion = conclusion;
            existing.RunAttempt = runAttempt;
            existing.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var runs = await dbContext.QualityGateRuns
            .AsNoTracking()
            .Where(run => run.Repository == repository && run.HeadSha == headSha)
            .ToListAsync(cancellationToken);

        var gateStatus = "success";
        var reasonCode = "all-required-workflows-succeeded";
        var reason = "All required workflows passed.";

        foreach (var required in configuration.RequiredWorkflows)
        {
            var run = runs.FirstOrDefault(candidate => string.Equals(candidate.WorkflowName, required, StringComparison.Ordinal));
            if (run is null)
            {
                gateStatus = "pending";
                reasonCode = "required-workflow-missing";
                reason = $"Waiting for required workflow '{required}'.";
                break;
            }

            if (!string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                gateStatus = "pending";
                reasonCode = "required-workflow-running";
                reason = $"Required workflow '{required}' is still running.";
                break;
            }

            if (!configuration.AllowedConclusions.Contains(run.Conclusion ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                gateStatus = "failure";
                reasonCode = "required-workflow-failed";
                reason = $"Required workflow '{required}' concluded '{run.Conclusion}'.";
                break;
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var evaluation = await dbContext.QualityGateEvaluations.FirstOrDefaultAsync(
            candidate => candidate.Repository == repository && candidate.HeadSha == headSha,
            cancellationToken);

        if (evaluation is null)
        {
            evaluation = new QualityGateEvaluation
            {
                Id = Guid.CreateVersion7(),
                Repository = repository,
                HeadSha = headSha,
                CreatedAtUtc = now
            };
            dbContext.QualityGateEvaluations.Add(evaluation);
        }

        evaluation.Status = gateStatus;
        evaluation.ReasonCode = reasonCode;
        evaluation.Reason = reason;
        evaluation.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var published = await statusClient.PostAsync(repository, headSha, gateStatus, reason, cancellationToken);
            if (!published)
            {
                logger.LogWarning("Quality gate status could not be published for {Repository}/{Sha}.", repository, headSha);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Quality gate status publish failed for {Repository}/{Sha}.", repository, headSha);
        }
    }
}
