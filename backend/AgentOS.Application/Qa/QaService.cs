using System.Collections.Concurrent;
using AgentOS.Application.CodingRuns;
using AgentOS.Domain.CodingRuns;
using AgentOS.Domain.Qa;
using Microsoft.Extensions.Logging;

namespace AgentOS.Application.Qa;

/// <summary>
/// Command-level QA for a terminal Coding Run. It reuses the validation-results artifact produced
/// by AOS-013b and never re-runs commands or modifies code.
/// </summary>
public sealed class QaService(
    ICodingRunRepository codingRuns,
    IQaRepository repository,
    TimeProvider timeProvider,
    ILogger<QaService> logger)
{
    public async Task<QaResult> GetLatestAsync(Guid codingRunId, CancellationToken cancellationToken)
    {
        var latest = await repository.GetLatestAsync(codingRunId, cancellationToken);
        return latest is null ? QaResult.NotFound() : QaResult.Ok(latest);
    }

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public async Task<QaResult> CreateAsync(Guid codingRunId, CancellationToken cancellationToken)
    {
        var gate = Locks.GetOrAdd(codingRunId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await CreateCoreAsync(codingRunId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<QaResult> CreateCoreAsync(Guid codingRunId, CancellationToken cancellationToken)
    {
        var run = await codingRuns.GetRunAsync(codingRunId, cancellationToken);
        if (run is null)
        {
            return QaResult.NotFound();
        }

        if (run.Status is not (CodingRunStatus.Succeeded or CodingRunStatus.Failed))
        {
            return QaResult.Conflict("coding-run-not-terminal");
        }

        if (run.TaskId is not { } taskId)
        {
            return QaResult.Conflict("coding-run-task-missing");
        }

        IReadOnlyList<ValidationCommandRecord>? commands;
        try
        {
            commands = await repository.GetValidationResultsAsync(taskId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "QA could not read validation results for run {RunId}.", codingRunId);
            return QaResult.Conflict("validation-results-invalid");
        }

        if (commands is null)
        {
            return QaResult.Conflict("validation-results-missing");
        }

        if (commands.Count == 0)
        {
            return QaResult.Conflict("validation-results-empty");
        }

        var reports = await repository.GetReportsAsync(taskId, cancellationToken);
        var parsed = QaReportParser.Parse(reports);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var attempt = await repository.NextAttemptAsync(codingRunId, cancellationToken) + 1;
        var qaId = Guid.CreateVersion7();

        var passed = commands.Count(command => string.Equals(command.Status, "Passed", StringComparison.OrdinalIgnoreCase));
        var failed = commands.Count - passed;
        var conclusion = failed == 0
            ? QaConclusion.Passed
            : QaConclusion.Failed;

        var runRecord = new QaRun
        {
            Id = qaId,
            CodingRunId = codingRunId,
            AttemptNo = attempt,
            Status = QaStatus.Completed,
            Conclusion = conclusion,
            CommandCount = commands.Count,
            PassedCount = passed,
            FailedCount = failed,
            CreatedAtUtc = now
        };

        var results = commands
            .Select((command, index) => new QaCommandResult
            {
                Id = Guid.CreateVersion7(),
                QaRunId = qaId,
                Ordinal = index,
                CommandText = Truncate(command.Command, 1000),
                Status = Truncate(command.Status, 20),
                ExitCode = command.ExitCode,
                DurationMs = command.DurationMs,
                OutputExcerpt = Truncate(command.Output, 4000)
            })
            .ToList();

        await repository.AddAsync(runRecord, results, parsed.TestCases, cancellationToken);
        return QaResult.Ok((await repository.GetLatestAsync(codingRunId, cancellationToken))!);
    }

    private static string Truncate(string? value, int max)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}
