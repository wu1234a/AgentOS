using AgentOS.Domain.Projects;

namespace AgentOS.Application.Projects;

public sealed class CreateProjectRequest
{
    public string? Name { get; init; }
    public string? Slug { get; init; }
    public string? Goal { get; init; }
    public string? Brief { get; init; }
    public string? PersonalNotes { get; init; }
    public string? AcceptanceCriteria { get; init; }
    public string? Repository { get; init; }
    public string? Status { get; init; }
    public int? MaxConcurrentTasks { get; init; }
    public bool? IsEnabled { get; init; }
}

public sealed class UpdateProjectRequest
{
    public string? Name { get; init; }
    public string? Goal { get; init; }
    public string? Brief { get; init; }
    public string? PersonalNotes { get; init; }
    public string? AcceptanceCriteria { get; init; }
    public string? Repository { get; init; }
    public string? Status { get; init; }
    public int? MaxConcurrentTasks { get; init; }
}

public sealed class SuggestProjectRequest
{
    public string? Goal { get; init; }
    public string? Notes { get; init; }
    public string? Repository { get; init; }
}

public sealed record ProjectSuggestion(
    string Name,
    string Slug,
    string Goal,
    string Brief,
    string AcceptanceCriteria,
    string? Repository,
    string Status);

public sealed record ProjectView(
    Guid Id,
    string Name,
    string Slug,
    bool IsEnabled,
    string? Goal,
    string? Brief,
    string? PersonalNotes,
    string? AcceptanceCriteria,
    string? Repository,
    string Status,
    int? MaxConcurrentTasks,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken);
    Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);
    Task<bool> TryAddAsync(Project project, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}

public enum ProjectOutcome { Ok = 0, NotFound = 1, Conflict = 2, ValidationFailed = 3 }

public sealed record ProjectResult(
    ProjectOutcome Outcome,
    ProjectView? Project,
    string? ErrorCode,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static ProjectResult Ok(ProjectView view) => new(ProjectOutcome.Ok, view, null, Empty);
    public static ProjectResult NotFound() => new(ProjectOutcome.NotFound, null, "project-not-found", Empty);
    public static ProjectResult Conflict(string code) => new(ProjectOutcome.Conflict, null, code, Empty);
    public static ProjectResult Invalid(string code, string field, string message)
        => new(ProjectOutcome.ValidationFailed, null, code, new Dictionary<string, string[]> { [field] = [message] });
    private static readonly Dictionary<string, string[]> Empty = [];
}

public sealed class ProjectService(
    IProjectRepository repository,
    TimeProvider timeProvider)
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draft", "Active", "OnHold", "Acceptance", "Completed", "Rework", "Archived"
    };

    public async Task<IReadOnlyList<ProjectView>> ListAsync(CancellationToken cancellationToken)
        => (await repository.ListAsync(cancellationToken)).Select(ToView).ToList();

    public async Task<ProjectView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await repository.GetAsync(id, cancellationToken);
        return project is null ? null : ToView(project);
    }

    public ProjectSuggestion Suggest(SuggestProjectRequest request)
    {
        var goal = TrimToNull(request.Goal) ?? string.Empty;
        var notes = TrimToNull(request.Notes);
        var oneLine = goal.ReplaceLineEndings(" ").Trim();
        var name = oneLine.Length <= 60 ? oneLine : oneLine[..60];
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "新项目";
        }

        var slug = $"project-{Guid.NewGuid():N}"[..20];
        var brief = $"""
            目标：
            {goal}

            个人想法：
            {notes ?? "（待补充）"}

            建议补充：
            - 范围与不做什么
            - 预算/周期/团队约束
            - 技术栈和不可改动项
            - 验收方式
            """;
        var acceptance = """
            - [ ] 目标已达成
            - [ ] 核心功能可用
            - [ ] 构建与测试通过
            - [ ] 文档已更新
            - [ ] 人工验收通过
            """;
        return new ProjectSuggestion(
            name,
            slug,
            goal,
            brief,
            acceptance,
            TrimToNull(request.Repository),
            "Draft");
    }

    public async Task<ProjectResult> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var slug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Length is < 1 or > 120)
        {
            return ProjectResult.Invalid("name-invalid", "name", "Name must be 1-120 characters.");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]{1,79}$"))
        {
            return ProjectResult.Invalid("slug-invalid", "slug", "Slug must be lowercase letters, digits and dashes.");
        }

        if (request.MaxConcurrentTasks is <= 0 or > 10000)
        {
            return ProjectResult.Invalid("quota-invalid", "maxConcurrentTasks", "Quota must be between 1 and 10000.");
        }

        var goal = TrimToNull(request.Goal);
        var brief = TrimToNull(request.Brief);
        var notes = TrimToNull(request.PersonalNotes);
        var acceptance = TrimToNull(request.AcceptanceCriteria);
        var repositoryName = TrimToNull(request.Repository);
        var status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim();
        if (goal is { Length: > 2000 })
        {
            return ProjectResult.Invalid("goal-invalid", "goal", "Goal must be 2000 characters or fewer.");
        }

        if (brief is { Length: > 8000 } || notes is { Length: > 8000 } || acceptance is { Length: > 8000 })
        {
            return ProjectResult.Invalid("brief-invalid", "brief", "Brief, notes and acceptance criteria must be 8000 characters or fewer.");
        }

        if (repositoryName is { Length: > 200 })
        {
            return ProjectResult.Invalid("repository-invalid", "repository", "Repository must be 200 characters or fewer.");
        }

        if (!AllowedStatuses.Contains(status))
        {
            return ProjectResult.Invalid("status-invalid", "status", "Status must be Draft, Active, OnHold, Acceptance, Completed, Rework or Archived.");
        }

        if (await repository.SlugExistsAsync(slug, cancellationToken))
        {
            return ProjectResult.Conflict("project-slug-exists");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Slug = slug,
            IsEnabled = request.IsEnabled ?? true,
            Goal = goal,
            Brief = brief,
            PersonalNotes = notes,
            AcceptanceCriteria = acceptance,
            Repository = repositoryName,
            Status = status,
            MaxConcurrentTasks = request.MaxConcurrentTasks,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        if (!await repository.TryAddAsync(project, cancellationToken))
        {
            return ProjectResult.Conflict("project-slug-exists");
        }

        return ProjectResult.Ok(ToView(project));
    }

    public async Task<ProjectResult> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await repository.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return ProjectResult.NotFound();
        }

        var name = request.Name?.Trim() ?? project.Name;
        if (name.Length is < 1 or > 120)
        {
            return ProjectResult.Invalid("name-invalid", "name", "Name must be 1-120 characters.");
        }

        if (request.MaxConcurrentTasks is int quota)
        {
            if (quota is <= 0 or > 10000)
            {
                return ProjectResult.Invalid("quota-invalid", "maxConcurrentTasks", "Quota must be between 1 and 10000.");
            }

            project.MaxConcurrentTasks = quota;
        }

        if (request.Goal is not null)
        {
            var goal = TrimToNull(request.Goal);
            if (goal is { Length: > 2000 })
            {
                return ProjectResult.Invalid("goal-invalid", "goal", "Goal must be 2000 characters or fewer.");
            }

            project.Goal = goal;
        }

        if (request.Brief is not null || request.PersonalNotes is not null || request.AcceptanceCriteria is not null)
        {
            var brief = request.Brief is null ? project.Brief : TrimToNull(request.Brief);
            var notes = request.PersonalNotes is null ? project.PersonalNotes : TrimToNull(request.PersonalNotes);
            var acceptance = request.AcceptanceCriteria is null ? project.AcceptanceCriteria : TrimToNull(request.AcceptanceCriteria);
            if (brief is { Length: > 8000 } || notes is { Length: > 8000 } || acceptance is { Length: > 8000 })
            {
                return ProjectResult.Invalid("brief-invalid", "brief", "Brief, notes and acceptance criteria must be 8000 characters or fewer.");
            }

            project.Brief = brief;
            project.PersonalNotes = notes;
            project.AcceptanceCriteria = acceptance;
        }

        if (request.Repository is not null)
        {
            var repositoryName = TrimToNull(request.Repository);
            if (repositoryName is { Length: > 200 })
            {
                return ProjectResult.Invalid("repository-invalid", "repository", "Repository must be 200 characters or fewer.");
            }

            project.Repository = repositoryName;
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            if (!AllowedStatuses.Contains(status))
            {
                return ProjectResult.Invalid("status-invalid", "status", "Status must be Draft, Active, OnHold, Acceptance, Completed, Rework or Archived.");
            }

            project.Status = status;
        }

        project.Name = name;
        project.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await repository.SaveAsync(cancellationToken);
        return ProjectResult.Ok(ToView(project));
    }

    public async Task<ProjectResult> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var project = await repository.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return ProjectResult.NotFound();
        }

        project.IsEnabled = enabled;
        project.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await repository.SaveAsync(cancellationToken);
        return ProjectResult.Ok(ToView(project));
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ProjectView ToView(Project project)
        => new(
            project.Id,
            project.Name,
            project.Slug,
            project.IsEnabled,
            project.Goal,
            project.Brief,
            project.PersonalNotes,
            project.AcceptanceCriteria,
            project.Repository,
            project.Status,
            project.MaxConcurrentTasks,
            project.CreatedAtUtc,
            project.UpdatedAtUtc);
}
