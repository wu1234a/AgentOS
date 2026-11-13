using System.Text.Json;
using AgentOS.Api.Backlog;
using AgentOS.Application.Planning;
using AgentOS.Application.Projects;
using AgentOS.Domain.Planning;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.ProjectPlanning;

public sealed class CreateProjectPlanRequest
{
    public Guid? ProjectId { get; init; }
    public string? Title { get; init; }
    public string? Goal { get; init; }
    public string? Constraints { get; init; }
    public int? TokenBudget { get; init; }
    public bool? RequiresReleaseApproval { get; init; }
    public BacklogDeliveryRequest? Delivery { get; init; }
}

public sealed class ReviseProjectPlanRequest
{
    public string? Feedback { get; init; }
}

public sealed class ReleaseProjectPlanRequest
{
    public Guid[]? BacklogItemIds { get; init; }
}

public sealed record ProjectPlanView(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Goal,
    string? Constraints,
    string Status,
    string ModelProfileCode,
    string? ProposalsJson,
    string? CritiquesJson,
    string? FinalPlanJson,
    string? Error,
    int Revision,
    string? Feedback,
    int TokenBudget,
    int TokensUsed,
    int PromptTokens,
    int CompletionTokens,
    decimal EstimatedCost,
    int ModelCalls,
    string? PreviousFinalPlanJson,
    int? PreviousRevision,
    bool RequiresReleaseApproval,
    DateTime? ReleaseApprovedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? DecidedAtUtc);

public sealed record ProjectPlanEpicProgress(
    string EpicKey,
    int Total,
    int Pending,
    int Hold,
    int Active,
    int Completed,
    int Failed);

public sealed record ProjectPlanItemProgress(
    Guid Id,
    string Title,
    string EpicKey,
    string Status);

public sealed record ProjectPlanProgressView(
    Guid PlanId,
    int Total,
    IReadOnlyList<ProjectPlanEpicProgress> Epics,
    IReadOnlyList<ProjectPlanItemProgress> Items);

public sealed record ProjectPlanReleaseResult(
    bool Success,
    int Released,
    ProjectPlanView? Plan,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record ProjectPlanCreateResult(
    bool Success,
    ProjectPlanView? Plan,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record ProjectPlanApprovalResult(
    bool Success,
    ProjectPlanView? Plan,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record ProjectPlanTask(
    string TaskKey,
    string Kind,
    string Epic,
    string Title,
    string Instruction,
    IReadOnlyList<string> DependsOn);

public sealed class ProjectPlanService(
    AgentOSDbContext dbContext,
    IProjectLookup projects,
    BacklogService backlog,
    IOptions<PlannerOptions> plannerOptions,
    TimeProvider timeProvider)
{
    private static readonly HashSet<string> AllowedKinds =
        new(StringComparer.OrdinalIgnoreCase) { "backend", "frontend", "docs", "full" };

    public async Task<ProjectPlanCreateResult> CreateAsync(
        CreateProjectPlanRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProjectId is not { } projectId)
        {
            return Fail("projectId is required.");
        }

        var project = await projects.FindByIdAsync(projectId, cancellationToken);
        if (project is null || !project.IsEnabled)
        {
            return Fail("Project does not exist or is disabled.");
        }

        var title = request.Title?.Trim() ?? string.Empty;
        var goal = request.Goal?.Trim() ?? string.Empty;
        if (title.Length is < 3 or > 200)
        {
            return Fail("title must be between 3 and 200 characters.");
        }

        if (goal.Length is < 10 or > 8000)
        {
            return Fail("goal must be between 10 and 8000 characters.");
        }

        var constraints = string.IsNullOrWhiteSpace(request.Constraints)
            ? null
            : request.Constraints.Trim();
        if (constraints is { Length: > 8000 })
        {
            return Fail("constraints must be 8000 characters or fewer.");
        }

        var delivery = request.Delivery ?? new BacklogDeliveryRequest();
        if (string.IsNullOrWhiteSpace(delivery.Repository))
        {
            return Fail("delivery.repository is required.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var plan = new ProjectPlan
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Title = title,
            Goal = goal,
            Constraints = constraints,
            DeliveryJson = JsonSerializer.Serialize(delivery),
            ModelProfileCode = plannerOptions.Value.DefaultModelProfileCode ?? "kimi-planner",
            TokenBudget = Math.Clamp(request.TokenBudget ?? 60_000, 10_000, 500_000),
            RequiresReleaseApproval = request.RequiresReleaseApproval ?? true,
            Status = "Queued",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.ProjectPlans.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProjectPlanCreateResult(true, ToView(plan), null, null);
    }

    public async Task<IReadOnlyList<ProjectPlanView>> ListAsync(
        string? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ProjectPlans.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(plan => plan.Status == status.Trim());
        }

        var plans = await query
            .OrderByDescending(plan => plan.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
        return plans.Select(ToView).ToList();
    }

    public async Task<ProjectPlanView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await dbContext.ProjectPlans.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return plan is null ? null : ToView(plan);
    }

    public async Task<ProjectPlanReleaseResult> ReleaseAsync(
        Guid id,
        ReleaseProjectPlanRequest? request,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.ProjectPlans.FirstOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (plan is null)
        {
            return new ProjectPlanReleaseResult(false, 0, null, "plan-not-found", "Project plan was not found.");
        }

        if (plan.Status != "Approved")
        {
            return new ProjectPlanReleaseResult(
                false,
                0,
                ToView(plan),
                "plan-not-approved",
                $"Project plan is {plan.Status}, not Approved.");
        }

        if (plan.RequiresReleaseApproval && plan.ReleaseApprovedAtUtc is null)
        {
            return new ProjectPlanReleaseResult(
                false,
                0,
                ToView(plan),
                "plan-release-approval-required",
                "This plan requires a second explicit release approval.");
        }

        var requestedIds = request?.BacklogItemIds?
            .Where(itemId => itemId != Guid.Empty)
            .Distinct()
            .ToArray() ?? [];
        var held = await dbContext.BacklogItems
            .Where(item => item.PlanId == plan.Id && item.Status == "Hold")
            .ToListAsync(cancellationToken);
        if (requestedIds.Length > 0)
        {
            var requested = held.Where(item => requestedIds.Contains(item.Id)).ToList();
            if (requested.Count != requestedIds.Length)
            {
                return new ProjectPlanReleaseResult(
                    false,
                    0,
                    ToView(plan),
                    "plan-items-not-held",
                    "Every selected Backlog item must belong to this plan and be Hold.");
            }

            held = requested;
        }

        if (held.Count == 0)
        {
            return new ProjectPlanReleaseResult(
                false,
                0,
                ToView(plan),
                "plan-no-held-tasks",
                "There are no held Backlog items to release.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var item in held)
        {
            item.Status = "Pending";
            item.UpdatedAtUtc = now;
        }

        plan.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProjectPlanReleaseResult(true, held.Count, ToView(plan), null, null);
    }

    public async Task<ProjectPlanApprovalResult> ApproveReleaseAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.ProjectPlans.FirstOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (plan is null)
        {
            return new ProjectPlanApprovalResult(false, null, "plan-not-found", "Project plan was not found.");
        }

        if (plan.Status != "Approved")
        {
            return new ProjectPlanApprovalResult(
                false,
                ToView(plan),
                "plan-not-approved",
                $"Project plan is {plan.Status}, not Approved.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        plan.ReleaseApprovedAtUtc = now;
        plan.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProjectPlanApprovalResult(true, ToView(plan), null, null);
    }

    public async Task<ProjectPlanProgressView?> GetProgressAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.ProjectPlans.AsNoTracking()
            .AnyAsync(plan => plan.Id == id, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var items = await dbContext.BacklogItems.AsNoTracking()
            .Where(item => item.PlanId == id)
            .Select(item => new { item.Id, item.Title, item.EpicKey, item.Status })
            .ToListAsync(cancellationToken);

        var epics = items
            .GroupBy(item => string.IsNullOrWhiteSpace(item.EpicKey) ? "unassigned" : item.EpicKey!)
            .OrderBy(group => group.Key)
            .Select(group => new ProjectPlanEpicProgress(
                group.Key,
                group.Count(),
                group.Count(item => item.Status == "Pending"),
                group.Count(item => item.Status == "Hold"),
                group.Count(item => item.Status is "Dispatched" or "InProgress" or "RepairScheduled"),
                group.Count(item => item.Status == "Completed"),
                group.Count(item => item.Status is "Failed" or "TaskFailed" or "NeedsAttention")))
            .ToList();

        var itemViews = items
            .Select(item => new ProjectPlanItemProgress(
                item.Id,
                item.Title,
                string.IsNullOrWhiteSpace(item.EpicKey) ? "unassigned" : item.EpicKey!,
                item.Status))
            .OrderBy(item => item.EpicKey)
            .ThenBy(item => item.Title)
            .ToList();
        return new ProjectPlanProgressView(id, items.Count, epics, itemViews);
    }

    public async Task<ProjectPlanApprovalResult> ReviseAsync(
        Guid id,
        ReviseProjectPlanRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.ProjectPlans.FirstOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (plan is null)
        {
            return new ProjectPlanApprovalResult(false, null, "plan-not-found", "Project plan was not found.");
        }

        if (plan.Status is not ("PendingReview" or "Failed"))
        {
            return new ProjectPlanApprovalResult(
                false,
                ToView(plan),
                "plan-not-revisable",
                $"Project plan is {plan.Status}; only PendingReview or Failed plans can be revised.");
        }

        var feedback = request.Feedback?.Trim() ?? string.Empty;
        if (feedback.Length is < 5 or > 8000)
        {
            return new ProjectPlanApprovalResult(
                false,
                ToView(plan),
                "invalid-feedback",
                "feedback must be between 5 and 8000 characters.");
        }

        plan.Status = "Queued";
        plan.Error = null;
        plan.PreviousFinalPlanJson = plan.FinalPlanJson;
        plan.PreviousRevision = plan.Revision;
        plan.Revision++;
        plan.Feedback = feedback;
        plan.FinalPlanJson = null;
        plan.ProposalsJson = null;
        plan.CritiquesJson = null;
        plan.DecidedAtUtc = null;
        plan.ReleaseApprovedAtUtc = null;
        plan.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProjectPlanApprovalResult(true, ToView(plan), null, null);
    }

    public async Task<ProjectPlanApprovalResult> ApproveAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.ProjectPlans.FirstOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (plan is null)
        {
            return new ProjectPlanApprovalResult(false, null, "plan-not-found", "Project plan was not found.");
        }

        if (plan.Status != "PendingReview")
        {
            return new ProjectPlanApprovalResult(
                false,
                ToView(plan),
                "plan-not-pending",
                $"Project plan is {plan.Status}, not PendingReview.");
        }

        var tasks = ParseTasks(plan.FinalPlanJson);
        if (tasks.Count == 0)
        {
            return new ProjectPlanApprovalResult(
                false,
                ToView(plan),
                "plan-has-no-tasks",
                "The plan does not contain valid tasks.");
        }

        BacklogDeliveryRequest? delivery;
        try
        {
            delivery = JsonSerializer.Deserialize<BacklogDeliveryRequest>(plan.DeliveryJson);
        }
        catch (JsonException)
        {
            delivery = null;
        }

        if (delivery is null || string.IsNullOrWhiteSpace(delivery.Repository))
        {
            return new ProjectPlanApprovalResult(
                false,
                ToView(plan),
                "plan-delivery-missing",
                "The plan has no valid delivery configuration.");
        }

        var created = new List<string>();
        foreach (var task in tasks)
        {
            var commands = ValidationCommandsFor(task.Kind);
            if (commands.Length == 0)
            {
                continue;
            }

            var result = await backlog.CreateAsync(
                new CreateBacklogItemRequest
                {
                    ProjectId = plan.ProjectId,
                    PlanId = plan.Id,
                    EpicKey = task.Epic,
                    Title = task.Title,
                    Instruction = task.Instruction,
                    ValidationCommands = commands,
                    Delivery = delivery,
                    Priority = "normal"
                },
                cancellationToken);

            if (!result.Success)
            {
                return new ProjectPlanApprovalResult(
                    false,
                    ToView(plan),
                    "plan-backlog-create-failed",
                    $"Could not create task '{task.Title}'.");
            }

            created.Add(task.Title);
        }

        if (created.Count == 0)
        {
            return new ProjectPlanApprovalResult(
                false,
                ToView(plan),
                "plan-has-no-valid-tasks",
                "No valid tasks could be created.");
        }

        plan.Status = "Approved";
        plan.DecidedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        plan.UpdatedAtUtc = plan.DecidedAtUtc.Value;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProjectPlanApprovalResult(true, ToView(plan), null, null);
    }

    public async Task<ProjectPlanApprovalResult> RejectAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await dbContext.ProjectPlans.FirstOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (plan is null)
        {
            return new ProjectPlanApprovalResult(false, null, "plan-not-found", "Project plan was not found.");
        }

        if (plan.Status != "PendingReview")
        {
            return new ProjectPlanApprovalResult(
                false,
                ToView(plan),
                "plan-not-pending",
                $"Project plan is {plan.Status}, not PendingReview.");
        }

        plan.Status = "Rejected";
        plan.DecidedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        plan.UpdatedAtUtc = plan.DecidedAtUtc.Value;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProjectPlanApprovalResult(true, ToView(plan), null, null);
    }

    internal static IReadOnlyList<ProjectPlanTask> ParseTasks(string? finalPlanJson)
    {
        if (string.IsNullOrWhiteSpace(finalPlanJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(finalPlanJson);
            if (!document.RootElement.TryGetProperty("tasks", out var tasks)
                || tasks.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<ProjectPlanTask>();
            var ordinal = 0;
            foreach (var item in tasks.EnumerateArray())
            {
                ordinal++;
                if (result.Count >= 20
                    || item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("kind", out var kind)
                    || !item.TryGetProperty("title", out var title)
                    || !item.TryGetProperty("instruction", out var instruction)
                    || kind.ValueKind != JsonValueKind.String
                    || title.ValueKind != JsonValueKind.String
                    || instruction.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var kindValue = kind.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
                var titleValue = title.GetString()?.Trim() ?? string.Empty;
                var instructionValue = instruction.GetString()?.Trim() ?? string.Empty;
                var epic = item.TryGetProperty("epic", out var epicElement)
                    && epicElement.ValueKind == JsonValueKind.String
                        ? epicElement.GetString()?.Trim() ?? "default"
                        : "default";
                var taskKey = item.TryGetProperty("taskKey", out var keyElement)
                    && keyElement.ValueKind == JsonValueKind.String
                        ? keyElement.GetString()?.Trim() ?? $"T{ordinal}"
                        : $"T{ordinal}";
                var dependsOn = new List<string>();
                if (item.TryGetProperty("dependsOn", out var dependsElement)
                    && dependsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dependency in dependsElement.EnumerateArray())
                    {
                        if (dependsOn.Count >= 10 || dependency.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var value = dependency.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            dependsOn.Add(value);
                        }
                    }
                }

                if (!AllowedKinds.Contains(kindValue)
                    || titleValue.Length is < 3 or > 200
                    || instructionValue.Length is < 10 or > 20_000
                    || epic.Length > 100
                    || taskKey.Length is < 1 or > 40)
                {
                    continue;
                }

                result.Add(new ProjectPlanTask(
                    taskKey,
                    kindValue,
                    epic,
                    titleValue,
                    instructionValue,
                    dependsOn));
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string[] ValidationCommandsFor(string kind)
        => kind switch
        {
            "backend" => ["dotnet build 3dERP.slnx", "dotnet test 3dERP.slnx --no-build"],
            "frontend" => ["npm --prefix web ci", "npm --prefix web run build"],
            "docs" => ["dotnet build 3dERP.slnx"],
            "full" => ["dotnet build 3dERP.slnx", "dotnet test 3dERP.slnx --no-build", "npm --prefix web ci", "npm --prefix web run build"],
            _ => []
        };

    private static ProjectPlanCreateResult Fail(string message)
        => new(false, null, "invalid-plan", message);

    private static ProjectPlanView ToView(ProjectPlan plan)
        => new(
            plan.Id,
            plan.ProjectId,
            plan.Title,
            plan.Goal,
            plan.Constraints,
            plan.Status,
            plan.ModelProfileCode,
            plan.ProposalsJson,
            plan.CritiquesJson,
            plan.FinalPlanJson,
            plan.Error,
            plan.Revision,
            plan.Feedback,
            plan.TokenBudget,
            plan.TokensUsed,
            plan.PromptTokens,
            plan.CompletionTokens,
            plan.EstimatedCost,
            plan.ModelCalls,
            plan.PreviousFinalPlanJson,
            plan.PreviousRevision,
            plan.RequiresReleaseApproval,
            plan.ReleaseApprovedAtUtc,
            plan.CreatedAtUtc,
            plan.UpdatedAtUtc,
            plan.DecidedAtUtc);
}
