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

        if (!result.IsSuccess)
        {
            return Results.ValidationProblem(result.Errors);
        }

        return Results.Accepted(
            $"/api/v1/events/{result.EventId}",
            new
            {
                eventId = result.EventId,
                accepted = true,
                duplicate = false,
                status = EventStatus.Received.ToString()
            });
    }

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
