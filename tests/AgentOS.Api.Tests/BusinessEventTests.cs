using System.Text.Json;
using AgentOS.Api.Business;
using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentOS.Api.Tests;

public sealed class BusinessEventTests
{
    [Fact]
    public async Task Accepts_and_deduplicates_business_events()
    {
        var events = new EventService(
            new CreateEventValidator(),
            new EventPayloadHasher(),
            new DuplicateEventLogSampler(TimeProvider.System),
            new FakeEventRepository(),
            new FakeProjectLookup(),
            new FakeTaskDispatcher(),
            TimeProvider.System,
            NullLogger<EventService>.Instance);
        var service = new BusinessEventService(events);
        var request = new BusinessEventRequest
        {
            Source = "3derp",
            EventType = "order.created",
            Project = "3derp",
            DedupeKey = $"order-{Guid.NewGuid():N}",
            Summary = "Order created",
            Payload = JsonSerializer.SerializeToElement(new { orderId = 42 })
        };

        var (first, error) = await service.SubmitAsync(request, CancellationToken.None);
        Assert.Null(error);
        Assert.Equal(CreateEventOutcome.Created, first!.Outcome);

        var (second, _) = await service.SubmitAsync(request, CancellationToken.None);
        Assert.Equal(CreateEventOutcome.Duplicate, second!.Outcome);
    }

    [Fact]
    public async Task Applies_defaults_for_optional_fields()
    {
        var events = new EventService(
            new CreateEventValidator(), new EventPayloadHasher(), new DuplicateEventLogSampler(TimeProvider.System),
            new FakeEventRepository(), new FakeProjectLookup(), new FakeTaskDispatcher(), TimeProvider.System,
            NullLogger<EventService>.Instance);
        var service = new BusinessEventService(events);

        var (result, error) = await service.SubmitAsync(new BusinessEventRequest
        {
            Source = "emperor", EventType = "turn.ended", Project = "3derp",
            DedupeKey = $"turn-{Guid.NewGuid():N}"
        }, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(CreateEventOutcome.Created, result!.Outcome);
    }

    [Fact]
    public async Task Rejects_timestamps_without_timezone()
    {
        var events = new EventService(
            new CreateEventValidator(), new EventPayloadHasher(), new DuplicateEventLogSampler(TimeProvider.System),
            new FakeEventRepository(), new FakeProjectLookup(), new FakeTaskDispatcher(), TimeProvider.System,
            NullLogger<EventService>.Instance);
        var service = new BusinessEventService(events);

        var (result, error) = await service.SubmitAsync(new BusinessEventRequest
        {
            Source = "3derp", EventType = "order.created", Project = "3derp",
            DedupeKey = $"tz-{Guid.NewGuid():N}", OccurredAtUtc = new DateTime(2026, 11, 8, 1, 2, 3, DateTimeKind.Unspecified)
        }, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal("occurred-at-requires-timezone", error);
    }

    [Fact]
    public async Task Rejects_unknown_sources()
    {
        var service = new BusinessEventService(new EventService(
            new CreateEventValidator(), new EventPayloadHasher(), new DuplicateEventLogSampler(TimeProvider.System),
            new FakeEventRepository(), new FakeProjectLookup(), new FakeTaskDispatcher(), TimeProvider.System,
            NullLogger<EventService>.Instance));

        var (result, error) = await service.SubmitAsync(new BusinessEventRequest
        {
            Source = "unknown", EventType = "test", Project = "3derp", DedupeKey = "x"
        }, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal("source-not-allowed", error);
    }
}
