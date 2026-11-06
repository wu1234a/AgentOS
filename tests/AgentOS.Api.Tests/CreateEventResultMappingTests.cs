using System.Text.Json;
using AgentOS.Api.Endpoints;
using AgentOS.Application.Events;
using AgentOS.Domain.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AgentOS.Api.Tests;

/// <summary>
/// Verifies the HTTP contract of <c>POST /api/v1/events</c> (status codes and body shape) by
/// executing the endpoint's result mapping against a real <see cref="HttpContext"/>.
/// </summary>
public sealed class CreateEventResultMappingTests
{
    [Fact]
    public async Task Created_maps_to_202_with_location_and_accepted_true()
    {
        var eventId = Guid.CreateVersion7();

        var (status, body, context) = await Execute(
            EventsEndpoints.MapCreateResult(CreateEventResult.Accepted(eventId, EventStatus.Processed, 2)));

        Assert.Equal(StatusCodes.Status202Accepted, status);
        Assert.Equal($"/api/v1/events/{eventId}", context.Response.Headers.Location.ToString());
        Assert.Equal(eventId, body.GetProperty("eventId").GetGuid());
        Assert.True(body.GetProperty("accepted").GetBoolean());
        Assert.False(body.GetProperty("duplicate").GetBoolean());
        Assert.Equal("Processed", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("tasksCreated").GetInt32());
    }

    [Fact]
    public async Task Duplicate_maps_to_200_with_the_original_event_id()
    {
        var existingEventId = Guid.CreateVersion7();

        var (status, body, _) = await Execute(
            EventsEndpoints.MapCreateResult(CreateEventResult.Duplicate(existingEventId, EventStatus.Processed, 1)));

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(existingEventId, body.GetProperty("eventId").GetGuid());
        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.True(body.GetProperty("duplicate").GetBoolean());
        Assert.Equal("Processed", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("tasksCreated").GetInt32());
    }

    [Fact]
    public async Task Conflict_maps_to_409_problem_details_without_payload_leakage()
    {
        var attemptedEventId = Guid.CreateVersion7();
        var existingEventId = Guid.CreateVersion7();

        var (status, body, _) = await Execute(
            EventsEndpoints.MapCreateResult(CreateEventResult.Conflict(attemptedEventId, existingEventId)));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal("event.dedupe.conflict", body.GetProperty("code").GetString());
        Assert.Equal(existingEventId, body.GetProperty("existingEventId").GetGuid());
        Assert.Equal(attemptedEventId, body.GetProperty("attemptedEventId").GetGuid());
        Assert.Equal("Event deduplication conflict", body.GetProperty("title").GetString());
        Assert.False(body.TryGetProperty("payload", out _));
    }

    [Fact]
    public async Task Invalid_maps_to_400_with_field_errors()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["dedupeKey"] = ["dedupeKey is required."]
        };

        var (status, body, _) = await Execute(
            EventsEndpoints.MapCreateResult(CreateEventResult.Invalid(errors)));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal(
            "dedupeKey is required.",
            body.GetProperty("errors").GetProperty("dedupeKey")[0].GetString());
    }

    private static async Task<(int Status, JsonElement Body, HttpContext Context)> Execute(IResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        services.AddOptions();

        await using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);

        return (context.Response.StatusCode, document.RootElement.Clone(), context);
    }
}
