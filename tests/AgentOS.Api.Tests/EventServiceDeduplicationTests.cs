using System.Text.Json;
using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Events;
using AgentOS.Domain.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentOS.Api.Tests;

public sealed class EventServiceDeduplicationTests
{
    private readonly FakeEventRepository _repository = new();
    private readonly FakeProjectLookup _projects = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task New_event_is_created_with_a_payload_hash()
    {
        var service = CreateService();

        var result = await service.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal(CreateEventOutcome.Created, result.Outcome);
        Assert.Equal(EventStatus.Received, result.Status);
        var stored = await _repository.GetByIdAsync(result.EventId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.NotNull(stored!.PayloadHash);
        Assert.Equal(EventPayloadHasher.HashLength, stored.PayloadHash!.Length);
    }

    [Fact]
    public async Task Resubmitting_identical_content_returns_the_original_event_id()
    {
        var service = CreateService();

        var created = await service.CreateAsync(Request(), CancellationToken.None);
        var duplicate = await service.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal(CreateEventOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(created.EventId, duplicate.EventId);
        Assert.Equal(created.EventId, duplicate.ExistingEventId);
        Assert.Single(await ListAllAsync(service));
    }

    [Fact]
    public async Task Semantically_equal_payloads_are_treated_as_duplicates()
    {
        var service = CreateService();

        await service.CreateAsync(Request(payload: """{"b":2,"a":{"y":1,"x":2}}"""), CancellationToken.None);
        var duplicate = await service.CreateAsync(Request(payload: """{"a":{"x":2,"y":1},"b":2}"""), CancellationToken.None);

        Assert.Equal(CreateEventOutcome.Duplicate, duplicate.Outcome);
    }

    [Fact]
    public async Task Same_dedupe_key_with_different_payload_is_a_conflict_and_keeps_the_original()
    {
        var service = CreateService();

        var created = await service.CreateAsync(Request(payload: """{"description":"x"}"""), CancellationToken.None);
        var conflict = await service.CreateAsync(Request(payload: """{"description":"y"}"""), CancellationToken.None);

        Assert.Equal(CreateEventOutcome.Conflict, conflict.Outcome);
        Assert.Equal(created.EventId, conflict.ExistingEventId);
        Assert.NotEqual(created.EventId, conflict.EventId);

        var stored = await _repository.GetByIdAsync(created.EventId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Contains("x", stored!.PayloadJson);
        Assert.Single(await ListAllAsync(service));
    }

    [Fact]
    public async Task Legacy_row_without_hash_is_compared_and_backfilled()
    {
        var legacy = LegacyEvent(payloadJson: """{ "b": 2, "a": 1 }""");
        _repository.Seed(legacy);
        var service = CreateService();

        var result = await service.CreateAsync(Request(payload: """{"a":1,"b":2}"""), CancellationToken.None);

        Assert.Equal(CreateEventOutcome.Duplicate, result.Outcome);
        Assert.Equal(legacy.Id, result.EventId);

        var stored = await _repository.FindByDedupeKeyAsync(legacy.Source, legacy.DedupeKey, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(EventPayloadHasher.HashLength, stored!.PayloadHash!.Length);
    }

    [Fact]
    public async Task Legacy_row_with_different_content_is_a_conflict()
    {
        var legacy = LegacyEvent(payloadJson: """{"a":"legacy"}""");
        _repository.Seed(legacy);
        var service = CreateService();

        var result = await service.CreateAsync(Request(payload: """{"a":"new"}"""), CancellationToken.None);

        Assert.Equal(CreateEventOutcome.Conflict, result.Outcome);
        Assert.Equal(legacy.Id, result.ExistingEventId);
        Assert.Single(await ListAllAsync(service));
    }

    [Fact]
    public async Task Project_that_does_not_exist_is_still_rejected()
    {
        var service = CreateService();

        var result = await service.CreateAsync(Request(project: "missing"), CancellationToken.None);

        Assert.Equal(CreateEventOutcome.Invalid, result.Outcome);
        Assert.True(result.Errors.ContainsKey("project"));
    }

    private EventService CreateService() => new(
        new CreateEventValidator(),
        new EventPayloadHasher(),
        new DuplicateEventLogSampler(_time),
        _repository,
        _projects,
        _time,
        NullLogger<EventService>.Instance);

    private async Task<IReadOnlyList<EventListItem>> ListAllAsync(EventService service)
    {
        var page = await service.ListAsync(new EventListQuery(1, 100, null, null, null, null), CancellationToken.None);
        return page.Items;
    }

    private static CreateEventRequest Request(
        string dedupeKey = "3derp:DEV-1",
        string payload = """{"description":"x"}""",
        string? project = "3derp") => new()
    {
        SchemaVersion = "1.0",
        Source = "3derp",
        EventType = "development.task.created",
        Project = project,
        Summary = "test event",
        DedupeKey = dedupeKey,
        OccurredAtUtc = "2026-11-05T00:00:00Z",
        Subject = new EventSubjectRequest { Type = "development_task", Id = "DEV-1" },
        Payload = JsonSerializer.Deserialize<JsonElement>(payload)
    };

    private static Event LegacyEvent(string payloadJson) => new()
    {
        Id = Guid.CreateVersion7(),
        Source = "3derp",
        EventType = "development.task.created",
        SchemaVersion = "1.0",
        DedupeKey = "3derp:DEV-1",
        Summary = "legacy event",
        PayloadJson = payloadJson,
        PayloadHash = null,
        Priority = "normal",
        Status = EventStatus.Received,
        OccurredAtUtc = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc),
        ReceivedAtUtc = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc)
    };
}
