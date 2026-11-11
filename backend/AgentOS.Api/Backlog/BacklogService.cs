using System.Text.Json;
using AgentOS.Application.Projects;
using AgentOS.Domain.Backlog;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.Backlog;

public sealed class CreateBacklogItemRequest
{
    public Guid? ProjectId { get; init; }
    public string? Title { get; init; }
    public string? Instruction { get; init; }
    public string[]? ValidationCommands { get; init; }
    public BacklogDeliveryRequest? Delivery { get; init; }
    public string? Priority { get; init; }
}

public sealed class BacklogDeliveryRequest
{
    public string? Repository { get; init; }
    public string? BaseBranch { get; init; } = "main";
    public string? Remote { get; init; } = "origin";
    public string? TokenEnvironmentVariable { get; init; } = "AGENTOS_GITHUB_TOKEN";
    public bool RequirePullRequest { get; init; } = true;
}

public sealed record BacklogItemView(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Status,
    string Priority,
    Guid? EventId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? DispatchedAtUtc,
    string? Error);

public sealed record BacklogCreateResult(
    bool Success,
    BacklogItemView? Item,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static BacklogCreateResult Ok(BacklogItemView item) => new(true, item, Empty);
    public static BacklogCreateResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(false, null, errors);
    private static readonly Dictionary<string, string[]> Empty = [];
}

public sealed class BacklogService(
    AgentOSDbContext dbContext,
    IProjectLookup projects,
    TimeProvider timeProvider)
{
    public async Task<BacklogCreateResult> CreateAsync(
        CreateBacklogItemRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.ProjectId is not { } projectId)
        {
            errors["projectId"] = ["projectId is required."];
        }

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is < 3 or > 200)
        {
            errors["title"] = ["title must be between 3 and 200 characters."];
        }

        var instruction = request.Instruction?.Trim() ?? string.Empty;
        if (instruction.Length is < 10 or > 20_000)
        {
            errors["instruction"] = ["instruction must be between 10 and 20000 characters."];
        }

        var priority = string.IsNullOrWhiteSpace(request.Priority) ? "normal" : request.Priority.Trim();
        if (priority is not ("low" or "normal" or "high" or "urgent"))
        {
            errors["priority"] = ["priority must be low, normal, high, or urgent."];
        }

        var commands = request.ValidationCommands ?? [];
        if (commands.Length > 20 || commands.Any(command => command.Length is 0 or > 1000))
        {
            errors["validationCommands"] = ["At most 20 non-empty commands of 1000 characters or fewer are allowed."];
        }

        if (request.Delivery is { } delivery
            && string.IsNullOrWhiteSpace(delivery.Repository))
        {
            errors["delivery.repository"] = ["delivery.repository is required when delivery is configured."];
        }

        if (errors.Count > 0 || request.ProjectId is not { } validProjectId)
        {
            return BacklogCreateResult.Invalid(errors);
        }

        var project = await projects.FindByIdAsync(validProjectId, cancellationToken);
        if (project is null || !project.IsEnabled)
        {
            return BacklogCreateResult.Invalid(new Dictionary<string, string[]>
            {
                ["projectId"] = ["Project does not exist or is disabled."]
            });
        }

        var id = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var branch = $"aos/backlog-{id:N}";
        var deliveryPayload = request.Delivery is { } configured
            ? new
            {
                enabled = true,
                repository = configured.Repository!.Trim(),
                baseBranch = string.IsNullOrWhiteSpace(configured.BaseBranch) ? "main" : configured.BaseBranch.Trim(),
                branch,
                commitMessage = title,
                pullRequestTitle = title,
                pullRequestBody = $"Backlog item {id}.",
                remote = string.IsNullOrWhiteSpace(configured.Remote) ? "origin" : configured.Remote.Trim(),
                tokenEnvironmentVariable = string.IsNullOrWhiteSpace(configured.TokenEnvironmentVariable)
                    ? "AGENTOS_GITHUB_TOKEN"
                    : configured.TokenEnvironmentVariable.Trim(),
                requirePullRequest = configured.RequirePullRequest
            }
            : null;

        var payload = JsonSerializer.SerializeToElement(new
        {
            instruction,
            validationCommands = commands,
            delivery = deliveryPayload
        });

        var item = new BacklogItem
        {
            Id = id,
            ProjectId = project.Id,
            Title = title,
            PayloadJson = payload.GetRawText(),
            Priority = priority,
            Status = "Pending",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.BacklogItems.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BacklogCreateResult.Ok(ToView(item));
    }

    public async Task<IReadOnlyList<BacklogItemView>> ListAsync(
        string? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.BacklogItems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status == status.Trim());
        }

        var items = await query
            .OrderBy(item => item.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
        return items.Select(ToView).ToList();
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.BacklogItems.FirstOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (item is null || item.Status != "Pending")
        {
            return false;
        }

        item.Status = "Cancelled";
        item.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static BacklogItemView ToView(BacklogItem item)
        => new(
            item.Id,
            item.ProjectId,
            item.Title,
            item.Status,
            item.Priority,
            item.EventId,
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            item.DispatchedAtUtc,
            item.Error);
}
