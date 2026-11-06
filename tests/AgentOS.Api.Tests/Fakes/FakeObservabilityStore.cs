using AgentOS.Application.Observability;
using AgentOS.Domain.Artifacts;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>Configurable observability read model for API contract tests.</summary>
public sealed class FakeObservabilityQueryStore : IObservabilityQueryStore
{
    public DashboardStats Dashboard { get; set; } = new(
        4, 1, 3, 1, 1, 1, 0, 0, 100, 1.5, 2.5, 1, 0, []);

    public TaskDetail? TaskDetail { get; set; }

    public List<TaskSummary> Tasks { get; } = [];

    public EventDetail? EventDetail { get; set; }

    public List<LogPage> LogPages { get; } = [];

    public List<WorkerView> Workers { get; } = [];

    public Task<DashboardStats> GetDashboardAsync(DateTime nowUtc, CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(Dashboard);

    public Task<(IReadOnlyList<EventListItem> Items, int Total)> ListEventsAsync(
        int limit, int offset, CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult<(IReadOnlyList<EventListItem>, int)>(([], 0));

    public Task<EventDetail?> GetEventAsync(Guid eventId, CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(EventDetail);

    public Task<(IReadOnlyList<TaskSummary> Items, int Total)> ListTasksAsync(
        string? status, Guid? projectId, int limit, int offset, CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult<(IReadOnlyList<TaskSummary>, int)>((Tasks, Tasks.Count));

    public Task<TaskDetail?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(TaskDetail);

    public Task<LogPage> GetTaskLogsAsync(
        Guid taskId, string? level, int afterSequence, int limit, CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(LogPages.FirstOrDefault() ?? new LogPage([], null));

    public Task<LogPage> GetAttemptLogsAsync(
        Guid taskId, Guid attemptId, string? level, int afterSequence, int limit,
        CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(LogPages.FirstOrDefault() ?? new LogPage([], null));

    public Task<IReadOnlyList<WorkerView>> ListWorkersAsync(CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<WorkerView>>(Workers);
}

/// <summary>In-memory artifact store for API contract tests.</summary>
public sealed class FakeArtifactStore : IArtifactStore
{
    public Dictionary<Guid, (Artifact Artifact, byte[] Content)> Artifacts { get; } = [];

    public async Task<Artifact> SaveAsync(
        Guid projectId,
        Guid taskId,
        Guid attemptId,
        Guid workerId,
        string fileName,
        string mediaType,
        Stream content,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var artifact = new Artifact
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            TaskId = taskId,
            AttemptId = attemptId,
            WorkerId = workerId,
            Name = Path.GetFileName(fileName),
            RelativePath = "test",
            Sha256 = new string('a', 64),
            SizeBytes = buffer.Length,
            MediaType = mediaType,
            CreatedAtUtc = DateTime.UtcNow
        };
        Artifacts[artifact.Id] = (artifact, buffer.ToArray());
        return artifact;
    }

    public Task<(Artifact Artifact, Stream Stream)?> OpenAsync(
        Guid artifactId, CancellationToken cancellationToken)
    {
        if (!Artifacts.TryGetValue(artifactId, out var entry))
        {
            return System.Threading.Tasks.Task.FromResult<(Artifact, Stream)?>(null);
        }

        return System.Threading.Tasks.Task.FromResult<(Artifact, Stream)?>((entry.Artifact, new MemoryStream(entry.Content)));
    }

    public Task<IReadOnlyList<ArtifactView>> ListAsync(Guid taskId, CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<ArtifactView>>(
            Artifacts.Values
                .Where(entry => entry.Artifact.TaskId == taskId)
                .Select(entry => new ArtifactView(
                    entry.Artifact.Id,
                    entry.Artifact.Name,
                    entry.Artifact.MediaType,
                    entry.Artifact.SizeBytes,
                    entry.Artifact.Sha256,
                    entry.Artifact.CreatedAtUtc))
                .ToList());
}
