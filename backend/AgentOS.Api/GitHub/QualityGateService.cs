using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public async Task EvaluateAsync(
        string repository,
        string headSha,
        string workflowName,
        long runId,
        string status,
        string? conclusion,
        int runAttempt,
        CancellationToken cancellationToken,
        bool publish = true)
    {
        var gate = Locks.GetOrAdd($"{repository}|{headSha}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await EvaluateCoreAsync(repository, headSha, workflowName, runId, status, conclusion, runAttempt, publish, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(bool Success, string? Error)> ReevaluateAsync(
        string repository,
        string headSha,
        CancellationToken cancellationToken)
    {
        if (!options.Value.QualityGates.TryGetValue(repository, out var configuration)
            || !configuration.Enabled
            || configuration.RequiredWorkflows.Length == 0)
        {
            return (false, "quality-gate-not-configured");
        }

        var runs = await statusClient.GetWorkflowRunsAsync(repository, headSha, cancellationToken);
        if (runs is null)
        {
            return (false, "github-actions-unavailable");
        }

        var selected = configuration.RequiredWorkflows
            .Select(required => runs
                .Where(run => string.Equals(run.Name, required, StringComparison.Ordinal))
                .OrderByDescending(run => run.Id)
                .ThenByDescending(run => run.RunAttempt)
                .FirstOrDefault())
            .Where(run => run is not null)
            .Select(run => run!)
            .ToList();

        for (var index = 0; index < selected.Count; index++)
        {
            var latest = selected[index];
            await EvaluateAsync(
                repository,
                headSha,
                latest.Name,
                latest.Id,
                latest.Status,
                latest.Conclusion,
                latest.RunAttempt,
                cancellationToken,
                publish: index == selected.Count - 1);
        }

        return (true, null);
    }

    public Task<(string Status, string? Reason)> CheckBranchProtectionAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken)
        => statusClient.GetBranchProtectionAsync(repository, branch, cancellationToken);

    private async Task EvaluateCoreAsync(
        string repository,
        string headSha,
        string workflowName,
        long runId,
        string status,
        string? conclusion,
        int runAttempt,
        bool publish,
        CancellationToken cancellationToken)
    {
        if (!options.Value.QualityGates.TryGetValue(repository, out var configuration) || !configuration.Enabled)
        {
            return;
        }

        var existing = await dbContext.QualityGateRuns.FirstOrDefaultAsync(
            run => run.Repository == repository && run.HeadSha == headSha && run.WorkflowName == workflowName,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.RunId > runId)
            {
                return;
            }

            if (existing.RunId == runId
                && (runAttempt < existing.RunAttempt
                    || (runAttempt == existing.RunAttempt
                        && existing.Status == "completed"
                        && status != "completed")))
            {
                return;
            }
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
        string? failureReason = null;
        string? pendingReason = null;

        foreach (var required in configuration.RequiredWorkflows)
        {
            var run = runs.FirstOrDefault(candidate => string.Equals(candidate.WorkflowName, required, StringComparison.Ordinal));
            if (run is null)
            {
                pendingReason ??= $"Waiting for required workflow '{required}'.";
                continue;
            }

            if (!string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                pendingReason ??= $"Required workflow '{required}' is still running.";
                continue;
            }

            if (!configuration.AllowedConclusions.Contains(run.Conclusion ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                failureReason ??= $"Required workflow '{required}' concluded '{run.Conclusion}'.";
            }
        }

        if (failureReason is not null)
        {
            gateStatus = "failure";
            reasonCode = "required-workflow-failed";
            reason = failureReason;
        }
        else if (pendingReason is not null)
        {
            gateStatus = "pending";
            reasonCode = "required-workflow-pending";
            reason = pendingReason;
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

        if (!publish)
        {
            return;
        }

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
