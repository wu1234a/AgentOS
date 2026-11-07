using AgentOS.Domain.Projects;

namespace AgentOS.Application.Projects;

public sealed class CreateProjectRequest
{
    public string? Name { get; init; }
    public string? Slug { get; init; }
    public int? MaxConcurrentTasks { get; init; }
    public bool? IsEnabled { get; init; }
}

public sealed class UpdateProjectRequest
{
    public string? Name { get; init; }
    public int? MaxConcurrentTasks { get; init; }
}

public sealed record ProjectView(
    Guid Id,
    string Name,
    string Slug,
    bool IsEnabled,
    int? MaxConcurrentTasks,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken);
    Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);
    Task AddAsync(Project project, CancellationToken cancellationToken);
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
    public async Task<IReadOnlyList<ProjectView>> ListAsync(CancellationToken cancellationToken)
        => (await repository.ListAsync(cancellationToken)).Select(ToView).ToList();

    public async Task<ProjectView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await repository.GetAsync(id, cancellationToken);
        return project is null ? null : ToView(project);
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
            MaxConcurrentTasks = request.MaxConcurrentTasks,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await repository.AddAsync(project, cancellationToken);
        await repository.SaveAsync(cancellationToken);
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

        if (request.MaxConcurrentTasks is <= 0 or > 10000)
        {
            return ProjectResult.Invalid("quota-invalid", "maxConcurrentTasks", "Quota must be between 1 and 10000.");
        }

        project.Name = name;
        project.MaxConcurrentTasks = request.MaxConcurrentTasks;
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

    private static ProjectView ToView(Project project)
        => new(project.Id, project.Name, project.Slug, project.IsEnabled, project.MaxConcurrentTasks, project.CreatedAtUtc, project.UpdatedAtUtc);
}
