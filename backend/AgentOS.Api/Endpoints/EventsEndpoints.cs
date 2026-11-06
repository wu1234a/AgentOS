using AgentOS.Api.Filters;
using AgentOS.Application.Events;
using AgentOS.Domain.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentOS.Api.Endpoints;

/// <summary>Event Hub endpoints introduced by AOS-002.</summary>
public static class EventsEndpoints
{
    private const int MaxPage = 1_000_000;
    private const int DefaultMaxRequestBodyBytes = 256 * 1024;

    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events")
            .AddEndpointFilter<ApiClientAuthenticationFilter>()
            .AddEndpointFilter(async (context, next) =>
            {
                var maxRequestBodyBytes = context.HttpContext.RequestServices
                    .GetRequiredService<IConfiguration>()
                    .GetValue("Events:MaxRequestBodyBytes", DefaultMaxRequestBodyBytes);

                if (context.HttpContext.Request.ContentLength > maxRequestBodyBytes)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: "Payload too large",
                        detail: $"The request body must not exceed {maxRequestBodyBytes} bytes.");
                }

                return await next(context);
            });

        group.MapPost("", CreateAsync).WithName("CreateEvent");
        group.MapGet("", ListAsync).WithName("ListEvents");
        group.MapGet("/{eventId:guid}", GetAsync).WithName("GetEvent");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateEventRequest request,
        EventService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return MapCreateResult(result);
    }

    /// <summary>
    /// Maps the application outcome to the documented HTTP contract:
    /// 202 for a new event, 200 for an identical duplicate, 409 for a dedupe-key conflict,
    /// and 400 for validation errors. Exposed to the test assembly so the contract can be
    /// verified without booting the test host.
    /// </summary>
    internal static IResult MapCreateResult(CreateEventResult result) => result.Outcome switch
    {
        CreateEventOutcome.Created => Results.Accepted(
            $"/api/v1/events/{result.EventId}",
            new
            {
                eventId = result.EventId,
                accepted = true,
                duplicate = false,
                status = result.Status!.Value.ToString()
            }),

        CreateEventOutcome.Duplicate => Results.Ok(new
        {
            eventId = result.EventId,
            accepted = false,
            duplicate = true,
            status = result.Status!.Value.ToString()
        }),

        CreateEventOutcome.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Event deduplication conflict",
            detail: "An event with the same source and dedupeKey already exists with different content. " +
                    "The stored event was not modified.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "event.dedupe.conflict",
                ["attemptedEventId"] = result.EventId,
                ["existingEventId"] = result.ExistingEventId
            }),

        _ => Results.ValidationProblem(result.Errors)
    };

    private static async Task<IResult> ListAsync(
        EventService service,
        int page = 1,
        int pageSize = 20,
        string? source = null,
        string? type = null,
        string? status = null,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (page < 1 || page > MaxPage)
        {
            errors["page"] = [$"page must be between 1 and {MaxPage}."];
        }

        if (pageSize < 1 || pageSize > 100)
        {
            errors["pageSize"] = ["pageSize must be between 1 and 100."];
        }

        if (!string.IsNullOrWhiteSpace(status)
            && !Enum.GetNames<EventStatus>().Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            errors["status"] = ["status is not a valid event status."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await service.ListAsync(
            new EventListQuery(page, pageSize, source, type, status, project),
            cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetAsync(
        Guid eventId,
        EventService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(eventId, cancellationToken);

        return result is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Event not found")
            : Results.Ok(result);
    }
}
