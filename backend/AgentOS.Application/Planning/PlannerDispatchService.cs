using System.Text.Json;
using AgentOS.Application.Events;
using AgentOS.Application.Projects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOS.Application.Planning;

public enum PlannerDispatchOutcome
{
    Disabled = 0,
    Skipped = 1,
    Dispatched = 2,
    Duplicate = 3,
    NoTask = 4,
    Failed = 5
}

public sealed record PlannerDispatchResult(
    PlannerDispatchOutcome Outcome,
    Guid? EventId = null,
    int TasksCreated = 0,
    string? ErrorCode = null,
    string? ErrorMessage = null);

/// <summary>
/// Turns an approved Planner draft into the standard event stream. A matching automation rule
/// then creates the task, so Planner approval keeps the same audit and deduplication path as
/// every other AgentOS trigger.
/// </summary>
public sealed class PlannerDispatchService(
    EventService events,
    IProjectLookup projects,
    IOptions<PlannerOptions> options,
    TimeProvider timeProvider,
    ILogger<PlannerDispatchService> logger)
{
    public async Task<PlannerDispatchResult> DispatchAsync(
        PlanningDraftView draft,
        CancellationToken cancellationToken)
    {
        var configuration = options.Value.AutoDispatch;
        if (!configuration.Enabled)
        {
            return new PlannerDispatchResult(PlannerDispatchOutcome.Disabled);
        }

        if (draft.ProjectId is null)
        {
            logger.LogInformation("Planner draft {DraftId} has no project; auto-dispatch skipped.", draft.Id);
            return new PlannerDispatchResult(PlannerDispatchOutcome.Skipped);
        }

        var project = await projects.FindByIdAsync(draft.ProjectId.Value, cancellationToken);
        if (project is null || !project.IsEnabled)
        {
            return new PlannerDispatchResult(
                PlannerDispatchOutcome.Failed,
                ErrorCode: "planner-dispatch-project-unavailable",
                ErrorMessage: "The draft project does not exist or is disabled.");
        }

        if (!string.Equals(project.Slug, configuration.ProjectSlug, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Planner draft {DraftId} belongs to project {ProjectSlug}, but auto-dispatch is configured for {ConfiguredSlug}; skipped.",
                draft.Id,
                project.Slug,
                configuration.ProjectSlug);
            return new PlannerDispatchResult(PlannerDispatchOutcome.Skipped);
        }

        if (string.IsNullOrWhiteSpace(configuration.Delivery.Repository))
        {
            return new PlannerDispatchResult(
                PlannerDispatchOutcome.Failed,
                ErrorCode: "planner-dispatch-delivery-unconfigured",
                ErrorMessage: "Planner auto-dispatch requires a delivery repository.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var branch = $"aos/planner-{draft.Id:N}";
        var instruction = $"""
            Implement the approved plan below in the project repository.

            ## Goal
            {draft.Goal}

            ## Approved plan
            {draft.ContentMarkdown}
            """;

        var payload = JsonSerializer.SerializeToElement(new
        {
            draftId = draft.Id,
            title = draft.Title,
            goal = draft.Goal,
            instruction,
            validationCommands = configuration.ValidationCommands,
            delivery = new
            {
                enabled = true,
                repository = configuration.Delivery.Repository,
                baseBranch = configuration.Delivery.BaseBranch,
                branch,
                commitMessage = draft.Title,
                pullRequestTitle = draft.Title,
                pullRequestBody = $"Approved Planner draft {draft.Id}.",
                remote = configuration.Delivery.Remote,
                tokenEnvironmentVariable = configuration.Delivery.TokenEnvironmentVariable,
                requirePullRequest = configuration.Delivery.RequirePullRequest
            }
        });

        var request = new CreateEventRequest
        {
            SchemaVersion = "1.0",
            Source = configuration.Source,
            EventType = configuration.EventType,
            ExternalEventId = $"planner-draft:{draft.Id}",
            Project = project.Slug,
            Subject = new EventSubjectRequest { Type = "planning-draft", Id = draft.Id.ToString() },
            Summary = draft.Title,
            OccurredAtUtc = now.ToString("O"),
            DedupeKey = $"planner:{draft.Id}",
            Priority = "normal",
            CorrelationId = $"planner:{draft.Id}",
            Payload = payload
        };

        var result = await events.CreateAsync(request, cancellationToken);
        if (result.Outcome is CreateEventOutcome.Conflict or CreateEventOutcome.Invalid)
        {
            logger.LogWarning(
                "Planner dispatch for draft {DraftId} failed: {Outcome}.",
                draft.Id,
                result.Outcome);
            return new PlannerDispatchResult(
                PlannerDispatchOutcome.Failed,
                result.EventId == Guid.Empty ? null : result.EventId,
                ErrorCode: "planner-dispatch-event-rejected",
                ErrorMessage: "The dispatch event was rejected.");
        }

        if (result.TasksCreated == 0)
        {
            logger.LogWarning(
                "Planner dispatch event {EventId} for draft {DraftId} matched no enabled rule.",
                result.EventId,
                draft.Id);
            return new PlannerDispatchResult(
                PlannerDispatchOutcome.NoTask,
                result.EventId,
                0,
                "planner-dispatch-no-rule",
                "The dispatch event was stored but no enabled rule created a task.");
        }

        logger.LogInformation(
            "Planner draft {DraftId} dispatched as event {EventId} with {TaskCount} task(s).",
            draft.Id,
            result.EventId,
            result.TasksCreated);

        return new PlannerDispatchResult(
            result.Outcome == CreateEventOutcome.Duplicate
                ? PlannerDispatchOutcome.Duplicate
                : PlannerDispatchOutcome.Dispatched,
            result.EventId,
            result.TasksCreated);
    }
}
