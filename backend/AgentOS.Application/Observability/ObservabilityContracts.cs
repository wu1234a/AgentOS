using AgentOS.Domain.Artifacts;

namespace AgentOS.Application.Observability;

public sealed record EventListItem(
    Guid Id,
    string Source,
    string Type,
    string Status,
    string? DedupeKey,
    DateTime OccurredAtUtc,
    DateTime ReceivedAtUtc,
    int TaskCount);

public sealed record TaskSummary(
    Guid Id,
    string TaskCode,
    string Title,
    string Status,
    string Priority,
    string ProjectSlug,
    string AgentCode,
    string? SkillCode,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    int AttemptCount,
    int MaxAttempts);

public sealed record EventDetail(
    EventListItem Event,
    string PayloadJson,
    IReadOnlyList<TaskSummary> Tasks);

public sealed record AttemptView(
    Guid Id,
    int AttemptNumber,
    Guid WorkerId,
    string WorkerCode,
    string Status,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    int? ExitCode,
    string? ErrorCode,
    string? ErrorMessage,
    int? DurationMs);

public sealed record TaskLogView(
    Guid Id,
    int Sequence,
    string Level,
    string Component,
    string Message,
    DateTime CreatedAtUtc);

public sealed record LogPage(IReadOnlyList<TaskLogView> Items, int? NextSequence);

public sealed record ArtifactView(
    Guid Id,
    string Name,
    string MediaType,
    long SizeBytes,
    string Sha256,
    DateTime CreatedAtUtc);

public sealed record UsageView(
    string Provider,
    string? Model,
    int InputTokens,
    int? CachedInputTokens,
    int OutputTokens,
    int? ReasoningTokens,
    int TotalTokens,
    string Source);

public sealed record TaskDetail(
    TaskSummary Task,
    IReadOnlyList<AttemptView> Attempts,
    IReadOnlyList<ArtifactView> Artifacts,
    UsageView? Usage,
    IReadOnlyList<TaskLogView> RecentLogs);

public sealed record WorkerView(
    Guid Id,
    string WorkerCode,
    string DisplayName,
    string Status,
    string Platform,
    string Architecture,
    string Version,
    DateTime? LastHeartbeatAtUtc,
    DateTime RegisteredAtUtc,
    string CapabilitiesJson);

public sealed record DashboardStats(
    int TotalEvents,
    int DuplicateEvents,
    int TotalTasks,
    int QueuedTasks,
    int RunningTasks,
    int SucceededTasks,
    int FailedTasks,
    int CancelledTasks,
    double SuccessRate,
    double? AverageWaitSeconds,
    double? AverageExecutionSeconds,
    int OnlineRunners,
    int LeaseExpiryCount,
    IReadOnlyList<TaskSummary> RecentFailures);

/// <summary>Thrown when an artifact upload violates the storage policy (maps to 400).</summary>
public sealed class ArtifactValidationException(string message) : Exception(message);

/// <summary>Read-side queries for the management UI; the database stays the source of truth.</summary>
public interface IObservabilityQueryStore
{
    Task<DashboardStats> GetDashboardAsync(DateTime nowUtc, CancellationToken cancellationToken);

    Task<(IReadOnlyList<EventListItem> Items, int Total)> ListEventsAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<EventDetail?> GetEventAsync(Guid eventId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<TaskSummary> Items, int Total)> ListTasksAsync(
        string? status,
        Guid? projectId,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<TaskDetail?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken);

    Task<LogPage> GetTaskLogsAsync(
        Guid taskId,
        string? level,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken);

    Task<LogPage> GetAttemptLogsAsync(
        Guid taskId,
        Guid attemptId,
        string? level,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkerView>> ListWorkersAsync(CancellationToken cancellationToken);
}

public sealed class ArtifactOptions
{
    public string RootPath { get; set; } = Path.Combine(Path.GetTempPath(), "agentos-artifacts");

    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    public string[] AllowedMediaTypes { get; set; } =
    [
        "text/plain",
        "text/markdown",
        "application/json",
        "application/octet-stream",
        "image/png",
        "image/jpeg"
    ];
}

/// <summary>Stores artifact bytes and metadata; clients never provide a storage path.</summary>
public interface IArtifactStore
{
    Task<Artifact> SaveAsync(
        Guid projectId,
        Guid taskId,
        Guid attemptId,
        Guid workerId,
        string fileName,
        string mediaType,
        Stream content,
        CancellationToken cancellationToken);

    Task<(Artifact Artifact, Stream Stream)?> OpenAsync(Guid artifactId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ArtifactView>> ListAsync(Guid taskId, CancellationToken cancellationToken);
}
