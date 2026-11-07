using System.Text.Json;
using System.Text.RegularExpressions;
using AgentOS.Application.Events;
using AgentOS.Domain.Scheduling;
using Microsoft.Extensions.Logging;

namespace AgentOS.Application.Scheduling;

public sealed class ScheduledJobService(
    IScheduledJobRepository repository,
    EventService events,
    TimeProvider timeProvider,
    ILogger<ScheduledJobService> logger)
{
    private const int MinimumIntervalSeconds = 60;
    private static readonly Regex EventTypePattern = new(
        "^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<ScheduledJobView>> ListAsync(CancellationToken cancellationToken)
        => (await repository.ListAsync(cancellationToken)).Select(ToView).ToList();

    public async Task<ScheduledJobView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        return job is null ? null : ToView(job);
    }

    public async Task<ScheduleResult> CreateAsync(CreateScheduledJobRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var eventType = request.EventType?.Trim() ?? string.Empty;
        var payloadJson = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson;
        var interval = request.IntervalSeconds ?? 0;
        if (name.Length is < 1 or > 120)
        {
            return ScheduleResult.Invalid("name", "Name must be 1-120 characters.");
        }

        if (!EventTypePattern.IsMatch(eventType))
        {
            return ScheduleResult.Invalid("eventType", "eventType must use dotted lowercase format.");
        }

        if (interval < MinimumIntervalSeconds)
        {
            return ScheduleResult.Invalid("intervalSeconds", $"Interval must be at least {MinimumIntervalSeconds} seconds.");
        }

        try
        {
            using var _ = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return ScheduleResult.Invalid("payloadJson", "payloadJson must be valid JSON.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var first = request.FirstRunAtUtc?.ToUniversalTime() ?? now.AddSeconds(interval);
        if (first < now)
        {
            first = now;
        }

        var job = new ScheduledJob
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            EventType = eventType,
            PayloadJson = payloadJson,
            IntervalSeconds = interval,
            FirstRunAtUtc = first,
            NextRunAtUtc = first,
            Enabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await repository.AddAsync(job, cancellationToken);
        await repository.SaveAsync(cancellationToken);
        return ScheduleResult.Ok(ToView(job));
    }

    public async Task<ScheduleResult> UpdateAsync(Guid id, UpdateScheduledJobRequest request, CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return ScheduleResult.NotFound();
        }

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length is < 1 or > 120)
            {
                return ScheduleResult.Invalid("name", "Name must be 1-120 characters.");
            }

            job.Name = name;
        }

        if (request.EventType is not null)
        {
            var eventType = request.EventType.Trim();
            if (!EventTypePattern.IsMatch(eventType))
            {
                return ScheduleResult.Invalid("eventType", "eventType must use dotted lowercase format.");
            }

            job.EventType = eventType;
        }

        if (request.PayloadJson is not null)
        {
            try
            {
                using var _ = JsonDocument.Parse(request.PayloadJson);
            }
            catch (JsonException)
            {
                return ScheduleResult.Invalid("payloadJson", "payloadJson must be valid JSON.");
            }

            job.PayloadJson = request.PayloadJson;
        }

        if (request.IntervalSeconds is int interval)
        {
            if (interval < MinimumIntervalSeconds)
            {
                return ScheduleResult.Invalid("intervalSeconds", $"Interval must be at least {MinimumIntervalSeconds} seconds.");
            }

            job.IntervalSeconds = interval;
            if (job.Enabled)
            {
                job.NextRunAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(interval);
            }
        }

        job.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await repository.SaveAsync(cancellationToken);
        return ScheduleResult.Ok(ToView(job));
    }

    public async Task<ScheduleResult> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return ScheduleResult.NotFound();
        }

        job.Enabled = enabled;
        if (enabled)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (job.NextRunAtUtc < now)
            {
                job.NextRunAtUtc = now;
            }
        }

        job.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await repository.SaveAsync(cancellationToken);
        return ScheduleResult.Ok(ToView(job));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return false;
        }

        repository.Remove(job);
        await repository.SaveAsync(cancellationToken);
        return true;
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var due = await repository.LoadDueAsync(now, 20, cancellationToken);
        var triggered = 0;
        foreach (var job in due)
        {
            var scheduledFor = job.NextRunAtUtc;
            var missed = (int)Math.Max(0, Math.Floor((now - scheduledFor).TotalSeconds / job.IntervalSeconds));
            var result = await PublishAsync(job, scheduledFor, now, missed, cancellationToken);
            if (result.Outcome == CreateEventOutcome.Invalid)
            {
                job.Enabled = false;
                job.UpdatedAtUtc = now;
                await repository.SaveAsync(cancellationToken);
                logger.LogWarning("Scheduled job {JobId} disabled because event validation failed.", job.Id);
                continue;
            }

            if (result.Outcome is CreateEventOutcome.Created or CreateEventOutcome.Duplicate or CreateEventOutcome.Conflict)
            {
                job.NextRunAtUtc = scheduledFor.AddSeconds((missed + 1) * (long)job.IntervalSeconds);
                while (job.NextRunAtUtc <= now)
                {
                    job.NextRunAtUtc = job.NextRunAtUtc.AddSeconds(job.IntervalSeconds);
                }

                job.LastTriggeredAtUtc = now;
                job.UpdatedAtUtc = now;
                await repository.SaveAsync(cancellationToken);
                triggered++;
            }
        }

        return triggered;
    }

    private async Task<CreateEventResult> PublishAsync(
        ScheduledJob job,
        DateTime scheduledFor,
        DateTime triggeredAt,
        int missed,
        CancellationToken cancellationToken)
    {
        JsonElement configured;
        try
        {
            using var document = JsonDocument.Parse(job.PayloadJson);
            configured = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            configured = JsonSerializer.SerializeToElement(new { });
        }

        var payload = JsonSerializer.SerializeToElement(new
        {
            scheduleId = job.Id,
            scheduledFor,
            triggeredAt,
            compensated = missed > 0,
            missedCount = missed,
            payload = configured
        });
        return await events.CreateAsync(new CreateEventRequest
        {
            SchemaVersion = "1.0",
            Source = "scheduler",
            EventType = job.EventType,
            ExternalEventId = $"{job.Id}:{scheduledFor:O}",
            DedupeKey = $"schedule:{job.Id}:{scheduledFor:O}",
            Subject = new EventSubjectRequest { Type = "scheduled-job", Id = job.Id.ToString() },
            Summary = $"Scheduled job '{job.Name}'",
            OccurredAtUtc = triggeredAt.ToString("O"),
            Priority = "normal",
            Payload = payload
        }, cancellationToken);
    }

    private static ScheduledJobView ToView(ScheduledJob job)
        => new(job.Id, job.Name, job.EventType, job.PayloadJson, job.IntervalSeconds, job.FirstRunAtUtc,
            job.NextRunAtUtc, job.Enabled, job.LastTriggeredAtUtc, job.CreatedAtUtc, job.UpdatedAtUtc);
}
