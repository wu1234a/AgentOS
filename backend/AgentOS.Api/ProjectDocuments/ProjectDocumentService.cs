using System.Text;
using System.Text.Json;
using AgentOS.Domain.Planning;
using AgentOS.Domain.Projects;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.ProjectDocuments;

public sealed record ProjectDocumentView(
    Guid Id,
    Guid ProjectId,
    Guid? PlanId,
    int PlanRevision,
    string DocumentType,
    string Title,
    int Version,
    string Status,
    DateTime CreatedAtUtc);

public sealed record ProjectDocumentDetail(
    Guid Id,
    Guid ProjectId,
    Guid? PlanId,
    int PlanRevision,
    string DocumentType,
    string Title,
    int Version,
    string Status,
    string ContentMarkdown,
    DateTime CreatedAtUtc);

public sealed class ProjectDocumentService(
    AgentOSDbContext dbContext,
    TimeProvider timeProvider)
{
    private static readonly string[] DocumentTypes =
    [
        "PROJECT", "BRAINSTORM", "ROADMAP", "ARCHITECTURE", "ACCEPTANCE"
    ];

    public async Task<IReadOnlyList<ProjectDocumentView>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var documents = await dbContext.ProjectDocuments.AsNoTracking()
            .Where(document => document.ProjectId == projectId)
            .OrderBy(document => document.DocumentType)
            .ThenByDescending(document => document.Version)
            .ToListAsync(cancellationToken);
        return documents.Select(ToView).ToList();
    }

    public async Task<ProjectDocumentDetail?> GetAsync(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var document = await dbContext.ProjectDocuments.AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.ProjectId == projectId && candidate.Id == documentId,
                cancellationToken);
        return document is null ? null : ToDetail(document);
    }

    /// <summary>Generates the five formal documents for an approved plan revision.</summary>
    public async Task CreateFromApprovedPlanAsync(
        ProjectPlan plan,
        Project project,
        CancellationToken cancellationToken)
    {
        var alreadyGenerated = await dbContext.ProjectDocuments
            .AnyAsync(
                document => document.PlanId == plan.Id && document.PlanRevision == plan.Revision,
                cancellationToken);
        if (alreadyGenerated)
        {
            return;
        }

        var finalPlan = ParseJson(plan.FinalPlanJson);
        var proposals = ParseJson(plan.ProposalsJson);
        var critiques = ParseJson(plan.CritiquesJson);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var contents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PROJECT"] = BuildProjectDocument(project),
            ["BRAINSTORM"] = BuildBrainstormDocument(project, proposals, critiques),
            ["ROADMAP"] = BuildRoadmapDocument(project, finalPlan),
            ["ARCHITECTURE"] = BuildArchitectureDocument(project, plan, finalPlan),
            ["ACCEPTANCE"] = BuildAcceptanceDocument(project, finalPlan)
        };

        foreach (var type in DocumentTypes)
        {
            var title = TitleFor(type);
            var version = await dbContext.ProjectDocuments
                .Where(document => document.ProjectId == project.Id && document.DocumentType == type)
                .MaxAsync(document => (int?)document.Version, cancellationToken) ?? 0;
            version++;

            var previous = await dbContext.ProjectDocuments
                .Where(document => document.ProjectId == project.Id
                    && document.DocumentType == type
                    && document.Status == "Approved")
                .ToListAsync(cancellationToken);
            foreach (var document in previous)
            {
                document.Status = "Superseded";
            }

            dbContext.ProjectDocuments.Add(new ProjectDocument
            {
                Id = Guid.CreateVersion7(),
                ProjectId = project.Id,
                PlanId = plan.Id,
                PlanRevision = plan.Revision,
                DocumentType = type,
                Title = title,
                Version = version,
                ContentMarkdown = contents[type],
                Status = "Approved",
                CreatedAtUtc = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildProjectDocument(Project project)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {project.Name} 项目章程");
        builder.AppendLine();
        builder.AppendLine("## 目标");
        builder.AppendLine(project.Goal ?? "（未填写）");
        builder.AppendLine();
        builder.AppendLine("## 项目简报");
        builder.AppendLine(project.Brief ?? "（未填写）");
        builder.AppendLine();
        builder.AppendLine("## 个人想法");
        builder.AppendLine(project.PersonalNotes ?? "（未填写）");
        builder.AppendLine();
        builder.AppendLine("## 验收标准");
        builder.AppendLine(project.AcceptanceCriteria ?? "（未填写）");
        builder.AppendLine();
        builder.AppendLine("## 仓库");
        builder.AppendLine(project.Repository ?? "（未填写）");
        return builder.ToString();
    }

    private static string BuildBrainstormDocument(
        Project project,
        JsonElement? proposals,
        JsonElement? critiques)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {project.Name} 头脑风暴与交叉评审");
        builder.AppendLine();
        builder.AppendLine("> 由规划委员会生成；原始内容保留在 ProjectPlan 贡献记录中。");
        builder.AppendLine();
        builder.AppendLine("## 独立提案");
        AppendRoleSections(builder, proposals);
        builder.AppendLine("## 交叉评审");
        AppendRoleSections(builder, critiques);
        return builder.ToString();
    }

    private static string BuildRoadmapDocument(Project project, JsonElement? finalPlan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {project.Name} 路线图");
        builder.AppendLine();
        if (finalPlan is { } plan
            && plan.ValueKind == JsonValueKind.Object
            && plan.TryGetProperty("recommendedOption", out var option))
        {
            builder.AppendLine("## 推荐方案");
            builder.AppendLine(option.GetString());
            builder.AppendLine();
        }

        builder.AppendLine("## 里程碑");
        if (finalPlan is { } root
            && root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("roadmap", out var roadmap)
            && roadmap.ValueKind == JsonValueKind.Array)
        {
            foreach (var milestone in roadmap.EnumerateArray())
            {
                builder.AppendLine($"### {GetString(milestone, "milestone")}");
                builder.AppendLine($"目标：{GetString(milestone, "goal")}");
                builder.AppendLine($"退出条件：{GetString(milestone, "exitCriteria")}");
                builder.AppendLine();
            }
        }
        else
        {
            builder.AppendLine("（无里程碑）");
        }

        builder.AppendLine("## 任务");
        if (finalPlan is { } taskRoot
            && taskRoot.ValueKind == JsonValueKind.Object
            && taskRoot.TryGetProperty("tasks", out var tasks)
            && tasks.ValueKind == JsonValueKind.Array)
        {
            foreach (var task in tasks.EnumerateArray())
            {
                builder.AppendLine(
                    $"- [{GetString(task, "epic")}] {GetString(task, "taskKey")} {GetString(task, "title")}");
            }
        }

        return builder.ToString();
    }

    private static string BuildArchitectureDocument(
        Project project,
        ProjectPlan plan,
        JsonElement? finalPlan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {project.Name} 技术方案");
        builder.AppendLine();
        builder.AppendLine("## 背景");
        builder.AppendLine(project.Brief ?? "（未填写）");
        builder.AppendLine();
        builder.AppendLine("## 约束");
        builder.AppendLine(plan.Constraints ?? "（未填写）");
        builder.AppendLine();
        builder.AppendLine("## Chair 综合理由");
        if (finalPlan is { } root && root.ValueKind == JsonValueKind.Object)
        {
            builder.AppendLine(GetString(root, "rationale"));
            builder.AppendLine();
            builder.AppendLine("## 风险");
            if (root.TryGetProperty("risks", out var risks) && risks.ValueKind == JsonValueKind.Array)
            {
                foreach (var risk in risks.EnumerateArray())
                {
                    builder.AppendLine($"- {GetString(risk, "risk")}：{GetString(risk, "mitigation")}");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## 任务技术指令");
            if (root.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
            {
                foreach (var task in tasks.EnumerateArray())
                {
                    builder.AppendLine($"### {GetString(task, "taskKey")} {GetString(task, "title")}");
                    builder.AppendLine(GetString(task, "instruction"));
                    builder.AppendLine();
                }
            }
        }
        else
        {
            builder.AppendLine("（无最终方案）");
        }

        return builder.ToString();
    }

    private static string BuildAcceptanceDocument(Project project, JsonElement? finalPlan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {project.Name} 验收标准");
        builder.AppendLine();
        builder.AppendLine("## 项目验收标准");
        builder.AppendLine(project.AcceptanceCriteria ?? "（未填写）");
        builder.AppendLine();
        builder.AppendLine("## 计划验收指标");
        if (finalPlan is { } root
            && root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("acceptanceMetrics", out var metrics)
            && metrics.ValueKind == JsonValueKind.Array)
        {
            foreach (var metric in metrics.EnumerateArray())
            {
                builder.AppendLine($"- [ ] {metric.GetString()}");
            }
        }
        else
        {
            builder.AppendLine("（无验收指标）");
        }

        return builder.ToString();
    }

    private static void AppendRoleSections(StringBuilder builder, JsonElement? value)
    {
        if (value is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            builder.AppendLine("（无记录）");
            builder.AppendLine();
            return;
        }

        foreach (var property in root.EnumerateObject())
        {
            builder.AppendLine($"### {property.Name}");
            builder.AppendLine(property.Value.GetString() ?? string.Empty);
            builder.AppendLine();
        }
    }

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

    private static string TitleFor(string type)
        => type switch
        {
            "PROJECT" => "项目章程",
            "BRAINSTORM" => "头脑风暴与评审",
            "ROADMAP" => "路线图",
            "ARCHITECTURE" => "技术方案",
            "ACCEPTANCE" => "验收标准",
            _ => type
        };

    private static ProjectDocumentView ToView(ProjectDocument document)
        => new(
            document.Id,
            document.ProjectId,
            document.PlanId,
            document.PlanRevision,
            document.DocumentType,
            document.Title,
            document.Version,
            document.Status,
            document.CreatedAtUtc);

    private static ProjectDocumentDetail ToDetail(ProjectDocument document)
        => new(
            document.Id,
            document.ProjectId,
            document.PlanId,
            document.PlanRevision,
            document.DocumentType,
            document.Title,
            document.Version,
            document.Status,
            document.ContentMarkdown,
            document.CreatedAtUtc);
}
