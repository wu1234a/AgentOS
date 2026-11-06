using System.Text.Json;
using AgentOS.Application.Projects;
using AgentOS.Domain.Events;
using AgentOS.Domain.Projects;
using Microsoft.Extensions.Logging;

namespace AgentOS.Application.Events;

/// <summary>Application use cases for accepting and reading events.</summary>
public sealed class EventService(
    CreateEventValidator validator,
    EventPayloadHasher payloadHasher,
    DuplicateEventLogSampler duplicateLogSampler,
    IEventRepository repository,
    IProjectLookup projectLookup,
    TimeProvider timeProvider,
    ILogger<EventService> logger)
{
    /// <summary>
    /// Accepts one event envelope. Idempotency is enforced by the database unique index on
    /// <c>Source + DedupeKey</c>: the insert is always attempted and the unique violation is
    /// translated into a duplicate (identical canonical payload) or a conflict (different
    /// payload) response.
    /// </summary>
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

        var canonicalPayload = payloadHasher.Canonicalize(request.Payload!.Value);

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
            PayloadJson = canonicalPayload.CanonicalJson,
            PayloadHash = canonicalPayload.Sha256,
            SubjectType = TrimToNull(request.Subject?.Type),
            SubjectId = TrimToNull(request.Subject?.Id),
            Priority = string.IsNullOrWhiteSpace(request.Priority)
                ? "normal"
                : request.Priority.Trim().ToLowerInvariant(),
            Status = EventStatus.Received,
            OccurredAtUtc = occurredAtUtc.UtcDateTime,
            ReceivedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            CorrelationId = TrimToNull(request.CorrelationId)
        };

        // Fast path: an ordinary retry finds the existing row here and never reaches the
        // failing INSERT, so EF Core does not emit an unsampled "SaveChanges failed" error
        // for an expected duplicate. The unique index below is still the authoritative
        // guard, and a concurrent winner that races past this read is handled by the
        // duplicate-key branch. A read is therefore an optimisation, never the defence.
        var existingByKey = await repository.FindByDedupeKeyAsync(entity.Source, entity.DedupeKey, cancellationToken);
        if (existingByKey is not null)
        {
            return await ResolveExistingAsync(existingByKey, canonicalPayload.Sha256, entity, cancellationToken);
        }

        var writeResult = await repository.TryAddAsync(entity, cancellationToken);
        if (writeResult.Outcome == EventWriteOutcome.Inserted)
        {
            return CreateEventResult.Accepted(entity.Id, EventStatus.Received);
        }

        var existing = writeResult.Existing
            ?? throw new InvalidOperationException(
                "The deduplication unique index rejected the insert but the existing event could not be loaded.");

        return await ResolveExistingAsync(existing, canonicalPayload.Sha256, entity, cancellationToken);
    }

    /// <summary>Compares an already stored event with the submitted payload.</summary>
    private async Task<CreateEventResult> ResolveExistingAsync(
        Event existing,
        string submittedHash,
        Event attempted,
        CancellationToken cancellationToken)
    {
        var existingHash = await ResolveExistingHashAsync(existing, cancellationToken);
        if (string.Equals(existingHash, submittedHash, StringComparison.Ordinal))
        {
            LogDuplicate(existing, attempted);
            return CreateEventResult.Duplicate(existing.Id, existing.Status);
        }

        LogConflict(existing, attempted);
        return CreateEventResult.Conflict(attempted.Id, existing.Id);
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

    /// <summary>
    /// Returns the stored payload hash, hashing (and lazily persisting) rows that were written
    /// before AOS-003 introduced the column.
    /// </summary>
    private async Task<string> ResolveExistingHashAsync(Event existing, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(existing.PayloadHash))
        {
            return existing.PayloadHash;
        }

        var hash = payloadHasher.CanonicalizeJson(existing.PayloadJson).Sha256;

        try
        {
            var updated = await repository.TrySetPayloadHashAsync(existing.Id, hash, cancellationToken);
            logger.LogInformation(
                "Backfilled PayloadHash for legacy event {EventId} (backfillApplied={BackfillApplied})",
                existing.Id,
                updated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed backfill must not change the response: the hash is recomputed next time.
            logger.LogWarning(ex, "Could not backfill PayloadHash for legacy event {EventId}", existing.Id);
        }

        return hash;
    }

    private void LogDuplicate(Event existing, Event attempted)
    {
        var decision = duplicateLogSampler.Observe(attempted.Source, attempted.DedupeKey);
        if (!decision.ShouldLog)
        {
            return;
        }

        logger.LogInformation(
            "Duplicate event ignored. Source={Source} DedupeKey={DedupeKey} EventId={EventId} OccurrencesInWindow={OccurrencesInWindow} SuppressedSinceLastLog={SuppressedSinceLastLog} CorrelationId={CorrelationId}",
            attempted.Source,
            attempted.DedupeKey,
            existing.Id,
            decision.OccurrencesInWindow,
            decision.SuppressedSinceLastLog,
            attempted.CorrelationId);
    }

    private void LogConflict(Event existing, Event attempted)
    {
        // Conflicts are logged on their own sampling channel: the first conflict in a window is
        // always recorded, even when the same key was retried many times as a duplicate before.
        var decision = duplicateLogSampler.Observe(attempted.Source, attempted.DedupeKey, isConflict: true);
        if (!decision.ShouldLog)
        {
            return;
        }

        logger.LogWarning(
            "Event deduplication conflict. Source={Source} DedupeKey={DedupeKey} ExistingEventId={ExistingEventId} AttemptedEventId={AttemptedEventId} OccurrencesInWindow={OccurrencesInWindow} SuppressedSinceLastLog={SuppressedSinceLastLog} CorrelationId={CorrelationId}",
            attempted.Source,
            attempted.DedupeKey,
            existing.Id,
            attempted.Id,
            decision.OccurrencesInWindow,
            decision.SuppressedSinceLastLog,
            attempted.CorrelationId);
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
