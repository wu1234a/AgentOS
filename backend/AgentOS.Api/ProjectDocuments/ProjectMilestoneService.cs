using System.Text;
using System.Text.Json;
using AgentOS.Domain.Planning;
using AgentOS.Domain.Projects;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.ProjectDocuments;

public sealed record ProjectMilestoneView(
    Guid Id,
    Guid ProjectId,
    Guid PlanId,
    string Key,
    string Title,
    string Goal,
    string ExitCriteria,
    string Status,
    DateTime? VerifiedAtUtc,
    string? VerifiedBy);

public sealed record ProjectAcceptanceResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage);

public sealed class ProjectMilestoneService(
    AgentOSDbContext dbContext,
    ProjectDocumentService documents,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<ProjectMilestoneView>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var milestones = await dbContext.ProjectMilestones.AsNoTracking()
            .Where(milestone => milestone.ProjectId == projectId)
            .OrderBy(milestone => milestone.Key)
            .ToListAsync(cancellationToken);
        return milestones.Select(ToView).ToList();
    }

    public async Task CreateFromPlanAsync(
        ProjectPlan plan,
        Project project,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.ProjectMilestones
            .AnyAsync(milestone => milestone.PlanId == plan.Id, cancellationToken);
        if (exists || string.IsNullOrWhiteSpace(plan.FinalPlanJson))
        {
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(plan.FinalPlanJson);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("roadmap", out var roadmap)
                || roadmap.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var item in roadmap.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var key = GetString(item, "milestone");
                var goal = GetString(item, "goal");
                var exit = GetString(item, "exitCriteria");
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                dbContext.ProjectMilestones.Add(new ProjectMilestone
                {
                    Id = Guid.CreateVersion7(),
                    ProjectId = project.Id,
                    PlanId = plan.Id,
                    Key = key.Length <= 40 ? key : key[..40],
                    Title = goal.Length > 200 ? goal[..200] : goal,
                    Goal = goal,
                    ExitCriteria = exit,
                    Status = "Pending",
                    CreatedAtUtc = now
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<ProjectMilestoneView?> VerifyAsync(
        Guid projectId,
        Guid milestoneId,
        string actor,
        CancellationToken cancellationToken)
    {
        var milestone = await dbContext.ProjectMilestones.FirstOrDefaultAsync(
            candidate => candidate.ProjectId == projectId && candidate.Id == milestoneId,
            cancellationToken);
        if (milestone is null)
        {
            return null;
        }

        milestone.Status = "Verified";
        milestone.VerifiedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        milestone.VerifiedBy = actor;
        await dbContext.SaveChangesAsync(cancellationToken);

        var allVerified = !await dbContext.ProjectMilestones.AnyAsync(
            candidate => candidate.ProjectId == projectId && candidate.Status != "Verified",
            cancellationToken);
        if (allVerified)
        {
            var project = await dbContext.Projects.FirstOrDefaultAsync(
                candidate => candidate.Id == projectId,
                cancellationToken);
            if (project is not null && project.Status == "Active")
            {
                project.Status = "Acceptance";
                project.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return ToView(milestone);
    }

    public async Task<ProjectAcceptanceResult> AcceptAsync(
        Guid projectId,
        string actor,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.FirstOrDefaultAsync(
            candidate => candidate.Id == projectId,
            cancellationToken);
        if (project is null)
        {
            return new ProjectAcceptanceResult(false, "project-not-found", "Project was not found.");
        }

        var milestones = await dbContext.ProjectMilestones
            .Where(milestone => milestone.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        if (milestones.Count == 0 || milestones.Any(milestone => milestone.Status != "Verified"))
        {
            return new ProjectAcceptanceResult(
                false,
                "project-milestones-incomplete",
                "All milestones must be verified before acceptance.");
        }

        project.Status = "Completed";
        project.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await documents.CreateAcceptanceReportAsync(project, milestones, actor, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProjectAcceptanceResult(true, null, null);
    }

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static ProjectMilestoneView ToView(ProjectMilestone milestone)
        => new(
            milestone.Id,
            milestone.ProjectId,
            milestone.PlanId,
            milestone.Key,
            milestone.Title,
            milestone.Goal,
            milestone.ExitCriteria,
            milestone.Status,
            milestone.VerifiedAtUtc,
            milestone.VerifiedBy);
}
