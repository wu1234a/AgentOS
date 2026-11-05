using System.Text.Json;
using AgentOS.Application.Projects;
using AgentOS.Domain.Events;
using AgentOS.Domain.Projects;

namespace AgentOS.Application.Events;

/// <summary>Application use cases for accepting and reading events.</summary>
public sealed class EventService(
    CreateEventValidator validator,
    IEventRepository repository,
    IProjectLookup projectLookup)
{
    public async Task<CreateEventResult> CreateAsync(
        CreateEventRequest request,
        CancellationToken cancellationToken)
    {
        var errors = validator.Validate(request);

        Project? project = null;
        var projectSlug = string.IsNullOrWhiteSpace(request.Project) ? null : request.Project.Trim();
        if (projectSlug is not null)
        {
            project = await projectLookup.FindBySlugAsync(projectSlug, cancellationToken);
            if (project is null)
            {
                errors = AddError(errors, "project", $"Project '{projectSlug}' was not found.");
            }
            else if (!project.IsEnabled)
            {
                errors = AddError(errors, "project", $"Project '{projectSlug}' is disabled.");
            }
        }

        if (errors.Count > 0)
        {
            return CreateEventResult.Invalid(errors);
        }

        if (!CreateEventValidator.TryParseOccurredAtUtc(request.OccurredAtUtc, out var occurredAtUtc))
        {
            throw new InvalidOperationException("occurredAtUtc was not validated.");
        }

        var entity = new Event
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project?.Id,
            Source = request.Source!.Trim(),
            EventType = request.EventType!.Trim(),
            SchemaVersion = request.SchemaVersion!.Trim(),
            ExternalEventId = TrimToNull(request.ExternalEventId),
            DedupeKey = request.DedupeKey!.Trim(),
            Summary = request.Summary!.Trim(),
            ResourceUrl = TrimToNull(request.ResourceUrl),
            PayloadJson = request.Payload!.Value.GetRawText(),
            SubjectType = TrimToNull(request.Subject?.Type),
            SubjectId = TrimToNull(request.Subject?.Id),
            Priority = string.IsNullOrWhiteSpace(request.Priority)
                ? "normal"
                : request.Priority.Trim().ToLowerInvariant(),
            Status = EventStatus.Received,
            OccurredAtUtc = occurredAtUtc.UtcDateTime,
            ReceivedAtUtc = DateTime.UtcNow,
            CorrelationId = TrimToNull(request.CorrelationId)
        };

        await repository.AddAsync(entity, cancellationToken);
        return CreateEventResult.Accepted(entity.Id);
    }

    public async Task<EventDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        return entity is null ? null : MapDetail(entity);
    }

    public async Task<EventListPage> ListAsync(EventListQuery query, CancellationToken cancellationToken)
    {
        var page = await repository.ListAsync(query, cancellationToken);
        return new EventListPage(
            page.Items.Select(MapListItem).ToArray(),
            query.Page,
            query.PageSize,
            page.Total);
    }

    private static EventListItem MapListItem(Event entity) => new(
        entity.Id,
        entity.ProjectId,
        entity.Project?.Slug,
        entity.Source,
        entity.EventType,
        entity.SchemaVersion,
        entity.ExternalEventId,
        entity.Summary,
        entity.ResourceUrl,
        entity.Status.ToString(),
        entity.ProcessedAtUtc,
        entity.SubjectType,
        entity.SubjectId,
        entity.Priority,
        entity.OccurredAtUtc,
        entity.ReceivedAtUtc,
        entity.CorrelationId);

    private static EventDetail MapDetail(Event entity)
    {
        using var document = JsonDocument.Parse(entity.PayloadJson);
        return new EventDetail(
            entity.Id,
            entity.ProjectId,
            entity.Project?.Slug,
            entity.Source,
            entity.EventType,
            entity.SchemaVersion,
            entity.ExternalEventId,
            entity.Summary,
            entity.ResourceUrl,
            entity.Status.ToString(),
            entity.ProcessedAtUtc,
            entity.SubjectType,
            entity.SubjectId,
            entity.Priority,
            entity.OccurredAtUtc,
            entity.ReceivedAtUtc,
            entity.CorrelationId,
            document.RootElement.Clone());
    }

    private static IReadOnlyDictionary<string, string[]> AddError(
        IReadOnlyDictionary<string, string[]> errors,
        string field,
        string message)
    {
        var copy = errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        copy[field] = [message];
        return copy;
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
