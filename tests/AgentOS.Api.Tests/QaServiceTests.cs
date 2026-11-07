using AgentOS.Application.CodingRuns;
using AgentOS.Application.Qa;
using AgentOS.Domain.CodingRuns;
using AgentOS.Domain.Planning;
using AgentOS.Domain.Qa;
using AgentOS.Domain.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentOS.Api.Tests;

public sealed class QaServiceTests
{
    [Fact]
    public async Task All_passed_commands_produce_passed_conclusion()
    {
        var run = new CodingRun { Id = Guid.NewGuid(), Status = CodingRunStatus.Succeeded, TaskId = Guid.NewGuid() };
        var repository = new FakeQaRepository
        {
            Commands =
            [
                new ValidationCommandRecord("dotnet test", "Passed", 0, 1200, "ok"),
                new ValidationCommandRecord("npm test", "Passed", 0, 800, "ok")
            ]
        };
        var service = CreateService(run, repository);

        var result = await service.CreateAsync(run.Id, CancellationToken.None);

        Assert.Equal(QaOutcome.Ok, result.Outcome);
        Assert.Equal("Passed", result.Qa!.Conclusion);
        Assert.Equal(2, result.Qa.PassedCount);
        Assert.Equal(2, result.Qa.Commands.Count);
    }

    [Fact]
    public async Task Failed_command_produces_failed_conclusion()
    {
        var run = new CodingRun { Id = Guid.NewGuid(), Status = CodingRunStatus.Failed, TaskId = Guid.NewGuid() };
        var repository = new FakeQaRepository
        {
            Commands = [new ValidationCommandRecord("dotnet test", "Failed", 1, 500, "fail")]
        };
        var service = CreateService(run, repository);

        var result = await service.CreateAsync(run.Id, CancellationToken.None);

        Assert.Equal("Failed", result.Qa!.Conclusion);
        Assert.Equal(1, result.Qa.FailedCount);
    }

    [Fact]
    public async Task Missing_validation_results_are_rejected()
    {
        var run = new CodingRun { Id = Guid.NewGuid(), Status = CodingRunStatus.Succeeded, TaskId = Guid.NewGuid() };
        var service = CreateService(run, new FakeQaRepository { Commands = null });

        var result = await service.CreateAsync(run.Id, CancellationToken.None);

        Assert.Equal(QaOutcome.Conflict, result.Outcome);
        Assert.Equal("validation-results-missing", result.ErrorCode);
    }

    [Fact]
    public async Task Non_terminal_run_is_rejected()
    {
        var run = new CodingRun { Id = Guid.NewGuid(), Status = CodingRunStatus.Running, TaskId = Guid.NewGuid() };
        var service = CreateService(run, new FakeQaRepository());

        var result = await service.CreateAsync(run.Id, CancellationToken.None);

        Assert.Equal(QaOutcome.Conflict, result.Outcome);
        Assert.Equal("coding-run-not-terminal", result.ErrorCode);
    }

    private static QaService CreateService(CodingRun run, FakeQaRepository repository)
        => new(
            new FakeCodingRunRepository(run),
            repository,
            TimeProvider.System,
            NullLogger<QaService>.Instance);

    private sealed class FakeQaRepository : IQaRepository
    {
        public IReadOnlyList<ValidationCommandRecord>? Commands { get; set; } = [];

        public QaView? Latest { get; private set; }

        public Task<QaView?> GetLatestAsync(Guid codingRunId, CancellationToken cancellationToken)
            => Task.FromResult(Latest);

        public Task<int> NextAttemptAsync(Guid codingRunId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<IReadOnlyList<ValidationCommandRecord>?> GetValidationResultsAsync(
            Guid taskId,
            CancellationToken cancellationToken)
            => Task.FromResult(Commands);

        public Task AddAsync(
            QaRun run,
            IReadOnlyList<QaCommandResult> commands,
            CancellationToken cancellationToken)
        {
            Latest = new QaView(
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
            return Task.CompletedTask;
        }
    }

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
}
