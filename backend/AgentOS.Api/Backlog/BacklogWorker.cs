using System.Text.Json;
using AgentOS.Application.Events;
using AgentOS.Application.Projects;
using AgentOS.Domain.Backlog;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DomainTaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Backlog;

/// <summary>
/// Dispatches the next pending backlog item for a project only when that project has no active
/// task. The item enters the normal event → rule → task path, so the existing Runner queue keeps
/// the execution and review flow unchanged.
/// </summary>
public sealed class BacklogWorker(
    IServiceScopeFactory scopes,
    IOptions<BacklogOptions> options,
    ILogger<BacklogWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Backlog worker is disabled.");
            return;
        }

        var pollSeconds = Math.Clamp(options.Value.PollSeconds, 5, 300);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await DispatchNextAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Backlog worker iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
        }
    }

    private async Task DispatchNextAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<AgentOSDbContext>();
        var events = services.GetRequiredService<EventService>();
        var projects = services.GetRequiredService<IProjectLookup>();

        var dispatched = await dbContext.BacklogItems
            .Where(item => item.Status == "Dispatched" && item.EventId != null)
            .ToListAsync(cancellationToken);
        foreach (var item in dispatched)
        {
            var task = await dbContext.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.EventId == item.EventId, cancellationToken);
            if (task is null)
            {
                continue;
            }

            if (task.Status == DomainTaskStatus.Succeeded)
            {
                item.Status = "Completed";
                item.Error = null;
                item.UpdatedAtUtc = DateTime.UtcNow;
                continue;
            }

            if (task.Status is not (DomainTaskStatus.Failed or DomainTaskStatus.Cancelled or DomainTaskStatus.Rejected))
            {
                continue;
            }

            var failureText = $"Task ended with {task.Status}: {task.ErrorMessage}".Trim();
            item.Error = failureText.Length <= 1000 ? failureText : failureText[..1000];
            item.UpdatedAtUtc = DateTime.UtcNow;
            if (task.Status == DomainTaskStatus.Failed
                && item.RepairAttempt < Math.Max(0, options.Value.MaxRepairAttempts))
            {
                var repair = BuildRepairItem(item, task.ErrorCode, task.ErrorMessage);
                if (repair is not null)
                {
                    dbContext.BacklogItems.Add(repair);
                    item.Status = "RepairScheduled";
                    logger.LogInformation(
                        "Scheduled repair item {RepairId} for failed backlog item {ItemId}.",
                        repair.Id,
                        item.Id);
                    continue;
                }
            }

            item.Status = "TaskFailed";
        }

        if (dispatched.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var pending = await dbContext.BacklogItems
            .Where(item => item.Status == "Pending")
            .OrderBy(item => item.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var group in pending.GroupBy(item => item.ProjectId))
        {
            var project = await projects.FindByIdAsync(group.Key, cancellationToken);
            if (project is null || !project.IsEnabled)
            {
                foreach (var item in group)
                {
                    item.Status = "Failed";
                    item.Error = "Project does not exist or is disabled.";
                    item.UpdatedAtUtc = DateTime.UtcNow;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            var active = await dbContext.Tasks.AnyAsync(
                task => task.ProjectId == group.Key
                    && task.Status != DomainTaskStatus.Succeeded
                    && task.Status != DomainTaskStatus.Failed
                    && task.Status != DomainTaskStatus.Cancelled
                    && task.Status != DomainTaskStatus.Rejected,
                cancellationToken);
            if (active)
            {
                continue;
            }

            var itemToDispatch = group.First();
            JsonElement payload;
            try
            {
                using var document = JsonDocument.Parse(itemToDispatch.PayloadJson);
                payload = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                itemToDispatch.Status = "Failed";
                itemToDispatch.Error = "Stored payload is invalid JSON.";
                itemToDispatch.UpdatedAtUtc = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            var request = new CreateEventRequest
            {
                SchemaVersion = "1.0",
                Source = "backlog",
                EventType = "task.requested",
                ExternalEventId = $"backlog:{itemToDispatch.Id}",
                Project = project.Slug,
                Subject = new EventSubjectRequest
                {
                    Type = "backlog-item",
                    Id = itemToDispatch.Id.ToString()
                },
                Summary = itemToDispatch.Title,
                OccurredAtUtc = DateTime.UtcNow.ToString("O"),
                DedupeKey = $"backlog:{itemToDispatch.Id}",
                Priority = itemToDispatch.Priority,
                CorrelationId = $"backlog:{itemToDispatch.Id}",
                Payload = payload
            };

            var result = await events.CreateAsync(request, cancellationToken);
            itemToDispatch.UpdatedAtUtc = DateTime.UtcNow;
            if (result.TasksCreated > 0)
            {
                itemToDispatch.Status = "Dispatched";
                itemToDispatch.EventId = result.EventId;
                itemToDispatch.DispatchedAtUtc = DateTime.UtcNow;
                itemToDispatch.Error = null;
                logger.LogInformation(
                    "Dispatched backlog item {ItemId} as event {EventId} with {TaskCount} task(s).",
                    itemToDispatch.Id,
                    result.EventId,
                    result.TasksCreated);
            }
            else
            {
                itemToDispatch.Status = "Failed";
                itemToDispatch.EventId = result.EventId == Guid.Empty ? null : result.EventId;
                itemToDispatch.Error = result.Outcome == CreateEventOutcome.Invalid
                    ? "Event validation failed."
                    : "No enabled automation rule created a task.";
                logger.LogWarning(
                    "Backlog item {ItemId} did not create a task (outcome {Outcome}).",
                    itemToDispatch.Id,
                    result.Outcome);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static BacklogItem? BuildRepairItem(
        BacklogItem item,
        string? errorCode,
        string? errorMessage)
    {
        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            var root = document.RootElement;
            var instruction = root.TryGetProperty("instruction", out var instructionElement)
                ? instructionElement.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(instruction))
            {
                return null;
            }

            var commands = new List<string>();
            if (root.TryGetProperty("validationCommands", out var commandsElement)
                && commandsElement.ValueKind == JsonValueKind.Array)
            {
                commands.AddRange(commandsElement.EnumerateArray()
                    .Select(command => command.GetString())
                    .Where(command => !string.IsNullOrWhiteSpace(command))
                    .Select(command => command!));
            }

            var deliveryElement = root.TryGetProperty("delivery", out var configuredDelivery)
                && configuredDelivery.ValueKind == JsonValueKind.Object
                    ? configuredDelivery
                    : (JsonElement?)null;

            var repairId = Guid.CreateVersion7();
            var title = item.Title + " (repair)";
            if (title.Length > 200)
            {
                title = title[..200];
            }

            object? deliveryPayload = null;
            if (deliveryElement is { } delivery)
            {
                deliveryPayload = new
                {
                    enabled = true,
                    repository = ReadString(delivery, "repository"),
                    baseBranch = ReadString(delivery, "baseBranch") ?? "main",
                    branch = $"aos/backlog-{repairId:N}",
                    commitMessage = title,
                    pullRequestTitle = title,
                    pullRequestBody = $"Repair for backlog item {item.Id}.",
                    remote = ReadString(delivery, "remote") ?? "origin",
                    tokenEnvironmentVariable = ReadString(delivery, "tokenEnvironmentVariable") ?? "AGENTOS_GITHUB_TOKEN",
                    requirePullRequest = ReadBool(delivery, "requirePullRequest") ?? true
                };
            }

            var failure = $"Error code: {errorCode ?? "unknown"}. Error: {errorMessage ?? "unknown"}";
            var repairInstruction = $"""
                {instruction}

                ## Previous attempt failed
                {failure}

                Fix the failure and complete the original task. Do not repeat the failed approach.
                """;

            var payload = JsonSerializer.SerializeToElement(new
            {
                instruction = repairInstruction,
                validationCommands = commands,
                delivery = deliveryPayload
            });

            var now = DateTime.UtcNow;
            return new BacklogItem
            {
                Id = repairId,
                ProjectId = item.ProjectId,
                Title = title,
                PayloadJson = payload.GetRawText(),
                Priority = item.Priority,
                Status = "Pending",
                RepairAttempt = item.RepairAttempt + 1,
                SourceItemId = item.SourceItemId ?? item.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
}
