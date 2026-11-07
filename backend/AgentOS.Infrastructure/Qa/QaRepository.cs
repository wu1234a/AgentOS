using System.Text.Json;
using AgentOS.Application.Observability;
using AgentOS.Application.Qa;
using AgentOS.Domain.Qa;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Qa;

public sealed class QaRepository(
    AgentOSDbContext dbContext,
    IArtifactStore artifactStore) : IQaRepository
{
    public async Task<QaView?> GetLatestAsync(Guid codingRunId, CancellationToken cancellationToken)
    {
        var run = await dbContext.QaRuns
            .AsNoTracking()
            .Where(candidate => candidate.CodingRunId == codingRunId)
            .OrderByDescending(candidate => candidate.AttemptNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (run is null)
        {
            return null;
        }

        var commands = await dbContext.QaCommandResults
            .AsNoTracking()
            .Where(result => result.QaRunId == run.Id)
            .OrderBy(result => result.Ordinal)
            .ToListAsync(cancellationToken);

        return new QaView(
            run.Id,
            run.CodingRunId,
            run.AttemptNo,
            run.Status.ToString(),
            run.Conclusion?.ToString(),
            run.CommandCount,
            run.PassedCount,
            run.FailedCount,
            run.ErrorSummary,
            run.CreatedAtUtc,
            commands
                .Select(command => new QaCommandView(
                    command.Ordinal,
                    command.CommandText,
                    command.Status,
                    command.ExitCode,
                    command.DurationMs,
                    command.OutputExcerpt))
                .ToList());
    }

    public async Task<int> NextAttemptAsync(Guid codingRunId, CancellationToken cancellationToken)
        => await dbContext.QaRuns
            .Where(run => run.CodingRunId == codingRunId)
            .Select(run => (int?)run.AttemptNo)
            .MaxAsync(cancellationToken) ?? 0;

    public async Task<IReadOnlyList<ValidationCommandRecord>?> GetValidationResultsAsync(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var artifact = await dbContext.Artifacts
            .AsNoTracking()
            .Where(candidate => candidate.TaskId == taskId && candidate.Name == "validation-results.json")
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (artifact is null)
        {
            return null;
        }

        var opened = await artifactStore.OpenAsync(artifact.Id, cancellationToken);
        if (opened is null)
        {
            return null;
        }

        await using var stream = opened.Value.Stream;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var records = new List<ValidationCommandRecord>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var command = item.TryGetProperty("command", out var commandValue)
                    && commandValue.ValueKind == JsonValueKind.String
                    ? commandValue.GetString() ?? string.Empty
                    : string.Empty;
                var status = item.TryGetProperty("status", out var statusValue)
                    && statusValue.ValueKind == JsonValueKind.String
                    ? statusValue.GetString() ?? "Failed"
                    : "Failed";
                var exitCode = item.TryGetProperty("exitCode", out var exitValue)
                    && exitValue.ValueKind == JsonValueKind.Number
                    && exitValue.TryGetInt32(out var parsedExit)
                    ? (int?)parsedExit
                    : null;
                var duration = item.TryGetProperty("durationMs", out var durationValue)
                    && durationValue.ValueKind == JsonValueKind.Number
                    && durationValue.TryGetInt32(out var parsedDuration)
                    ? (int?)parsedDuration
                    : null;
                var output = item.TryGetProperty("output", out var outputValue)
                    && outputValue.ValueKind == JsonValueKind.String
                    ? outputValue.GetString()
                    : null;

                records.Add(new ValidationCommandRecord(command, status, exitCode, duration, output));
            }

            return records;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task AddAsync(
        QaRun run,
        IReadOnlyList<QaCommandResult> commands,
        CancellationToken cancellationToken)
    {
        await dbContext.QaRuns.AddAsync(run, cancellationToken);
        await dbContext.QaCommandResults.AddRangeAsync(commands, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
