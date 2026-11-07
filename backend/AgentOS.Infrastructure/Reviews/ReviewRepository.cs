using System.Text.Json;
using AgentOS.Application.Observability;
using AgentOS.Application.Reviews;
using AgentOS.Domain.Reviews;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Reviews;

public sealed class ReviewRepository(
    AgentOSDbContext dbContext,
    IArtifactStore artifactStore) : IReviewRepository
{
    private const int MaxDiffBytes = 1_000_000;

    public Task<ReviewRun?> GetTrackedAsync(Guid codingRunId, CancellationToken cancellationToken)
        => dbContext.ReviewRuns.FirstOrDefaultAsync(
            review => review.CodingRunId == codingRunId,
            cancellationToken);

    public async Task<ReviewView?> GetViewAsync(Guid codingRunId, CancellationToken cancellationToken)
    {
        var review = await dbContext.ReviewRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.CodingRunId == codingRunId, cancellationToken);

        if (review is null)
        {
            return null;
        }

        var findings = await dbContext.ReviewFindings
            .AsNoTracking()
            .Where(finding => finding.ReviewRunId == review.Id)
            .OrderBy(finding => finding.SortOrder)
            .ToListAsync(cancellationToken);

        return new ReviewView(
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
            findings
                .Select(finding => new ReviewFindingView(
                    finding.Severity.ToString(),
                    finding.FilePath,
                    finding.StartLine,
                    finding.EndLine,
                    finding.Reason,
                    finding.Suggestion))
                .ToList());
    }

    public async Task<ReviewChangeSet?> GetChangeSetAsync(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var artifacts = await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.TaskId == taskId
                && (artifact.Name == "changeset.diff" || artifact.Name == "changeset.json"))
            .OrderByDescending(artifact => artifact.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var diffArtifact = artifacts.FirstOrDefault(artifact => artifact.Name == "changeset.diff");
        if (diffArtifact is null)
        {
            return null;
        }

        var opened = await artifactStore.OpenAsync(diffArtifact.Id, cancellationToken);
        if (opened is null)
        {
            return null;
        }

        await using var stream = opened.Value.Stream;
        using var reader = new StreamReader(stream);
        var diff = await reader.ReadToEndAsync(cancellationToken);
        if (diff.Length > MaxDiffBytes)
        {
            return null;
        }

        var files = new List<string>();
        string? baseRevision = null;
        string? headRevision = null;
        var metadataArtifact = artifacts.FirstOrDefault(artifact => artifact.Name == "changeset.json");

        if (metadataArtifact is not null)
        {
            var metadataOpened = await artifactStore.OpenAsync(metadataArtifact.Id, cancellationToken);
            if (metadataOpened is not null)
            {
                await using var metadataStream = metadataOpened.Value.Stream;
                using var metadataReader = new StreamReader(metadataStream);
                var json = await metadataReader.ReadToEndAsync(cancellationToken);

                try
                {
                    using var document = JsonDocument.Parse(json);
                    if (document.RootElement.TryGetProperty("baseRevision", out var baseValue))
                    {
                        baseRevision = baseValue.GetString();
                    }

                    if (document.RootElement.TryGetProperty("headRevision", out var headValue))
                    {
                        headRevision = headValue.GetString();
                    }

                    if (document.RootElement.TryGetProperty("files", out var filesValue)
                        && filesValue.ValueKind == JsonValueKind.Array)
                    {
                        files.AddRange(filesValue.EnumerateArray()
                            .Where(item => item.ValueKind == JsonValueKind.String)
                            .Select(item => item.GetString()!)
                            .Where(item => !string.IsNullOrWhiteSpace(item)));
                    }
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        return new ReviewChangeSet(diff, files, baseRevision, headRevision);
    }

    public async Task AddReviewAsync(ReviewRun review, CancellationToken cancellationToken)
        => await dbContext.ReviewRuns.AddAsync(review, cancellationToken);

    public async Task AddFindingsAsync(
        IReadOnlyList<ReviewFinding> findings,
        CancellationToken cancellationToken)
        => await dbContext.ReviewFindings.AddRangeAsync(findings, cancellationToken);

    public async Task DeleteFindingsAsync(Guid reviewId, CancellationToken cancellationToken)
        => await dbContext.ReviewFindings
            .Where(finding => finding.ReviewRunId == reviewId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
