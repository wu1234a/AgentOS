using System.Text.Json;
using AgentOS.Domain.Scheduling;

namespace AgentOS.Application.Scheduling;

public sealed class CreateScheduledJobRequest
{
    public string? Name { get; init; }
    public string? EventType { get; init; }
    public string? PayloadJson { get; init; }
    public int? IntervalSeconds { get; init; }
    public DateTime? FirstRunAtUtc { get; init; }
}

public sealed class UpdateScheduledJobRequest
{
    public string? Name { get; init; }
    public string? EventType { get; init; }
    public string? PayloadJson { get; init; }
    public int? IntervalSeconds { get; init; }
}

public sealed record ScheduledJobView(
    Guid Id,
    string Name,
    string EventType,
    string PayloadJson,
    int IntervalSeconds,
    DateTime FirstRunAtUtc,
    DateTime NextRunAtUtc,
    bool Enabled,
    DateTime? LastTriggeredAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public interface IScheduledJobRepository
{
    Task<IReadOnlyList<ScheduledJob>> ListAsync(CancellationToken cancellationToken);
    Task<ScheduledJob?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScheduledJob>> LoadDueAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken);
    Task AddAsync(ScheduledJob job, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    void Remove(ScheduledJob job);
}

public enum ScheduleOutcome { Ok = 0, NotFound = 1, Conflict = 2, ValidationFailed = 3 }

public sealed record ScheduleResult(
    ScheduleOutcome Outcome,
    ScheduledJobView? Job,
    string? ErrorCode,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static ScheduleResult Ok(ScheduledJobView job) => new(ScheduleOutcome.Ok, job, null, Empty);
    public static ScheduleResult NotFound() => new(ScheduleOutcome.NotFound, null, "schedule-not-found", Empty);
    public static ScheduleResult Conflict(string code) => new(ScheduleOutcome.Conflict, null, code, Empty);
    public static ScheduleResult Invalid(string field, string message)
        => new(ScheduleOutcome.ValidationFailed, null, "schedule-invalid", new Dictionary<string, string[]> { [field] = [message] });
    private static readonly Dictionary<string, string[]> Empty = [];
}
