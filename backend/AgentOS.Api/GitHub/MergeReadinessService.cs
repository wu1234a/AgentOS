using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.GitHub;

public sealed record MergeCheck(string Name, bool Passed, string Message);

public sealed record MergeReadinessResult(
    string Repository,
    int PullRequestNumber,
    string HeadSha,
    bool Ready,
    IReadOnlyList<MergeCheck> Checks);

public sealed class MergeReadinessService(
    AgentOSDbContext dbContext,
    IGitHubPullRequestClient client,
    IGitHubCommitStatusClient statusClient,
    IOptions<GitHubOptions> options)
{
    public async Task<MergeReadinessResult?> EvaluateAsync(
        string repository,
        int number,
        CancellationToken cancellationToken)
    {
        var policy = options.Value.MergePolicies.GetValueOrDefault(repository);
        if (policy is null || !policy.Enabled)
        {
            return null;
        }

        var mergeInfo = await client.GetMergeInfoAsync(repository, number, cancellationToken);
        if (mergeInfo is null)
        {
            return new MergeReadinessResult(repository, number, string.Empty, false,
                [new MergeCheck("github-api", false, "PR metadata is unavailable.")]);
        }

        var checks = new List<MergeCheck>
        {
            new("pr-open", mergeInfo.State == "open", $"PR state is {mergeInfo.State}."),
            new("not-draft", !mergeInfo.Draft, mergeInfo.Draft ? "PR is a draft." : "PR is ready for review."),
            new("mergeable", mergeInfo.Mergeable == true, mergeInfo.Mergeable != true ? "PR mergeability is unavailable or false." : $"mergeable_state={mergeInfo.MergeableState ?? "unknown"}.")
        };

        var approvals = await client.GetApprovalCountAsync(repository, number, cancellationToken);
        checks.Add(new MergeCheck(
            "approvals",
            approvals is not null && approvals >= policy.RequiredApprovals,
            approvals is null ? "Approval count is unavailable." : $"Approvals: {approvals}/{policy.RequiredApprovals}."));

        if (policy.RequireQualityGate)
        {
            var gate = await dbContext.QualityGateEvaluations
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    evaluation => evaluation.Repository == repository && evaluation.HeadSha == mergeInfo.HeadSha,
                    cancellationToken);
            checks.Add(new MergeCheck(
                "quality-gate",
                gate?.Status == "success",
                gate is null ? "No quality gate evaluation." : $"Quality gate is {gate.Status}: {gate.Reason}"));
        }

        if (policy.RequireBranchProtection)
        {
            var protection = await statusClient.GetBranchProtectionAsync(repository, mergeInfo.BaseRef, cancellationToken);
            checks.Add(new MergeCheck(
                "branch-protection",
                protection.Status == "required",
                $"agentos/quality-gate protection: {protection.Status}."));
        }

        return new MergeReadinessResult(
            repository,
            number,
            mergeInfo.HeadSha,
            checks.All(check => check.Passed),
            checks);
    }
}
