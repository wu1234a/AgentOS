using AgentOS.Application.Projects;
using AgentOS.Domain.Projects;

namespace AgentOS.Api.Tests.Fakes;

public sealed class FakeProjectLookup : IProjectLookup
{
    private readonly List<Project> _projects;

    public FakeProjectLookup()
    {
        Enabled = Create("3DERP", "3derp", isEnabled: true);
        Disabled = Create("Disabled Demo", "disabled-demo", isEnabled: false);
        _projects = [Enabled, Disabled];
    }

    public Project Enabled { get; }

    public Project Disabled { get; }

    public Task<Project?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
        => Task.FromResult(_projects.FirstOrDefault(project =>
            string.Equals(project.Slug, slug, StringComparison.OrdinalIgnoreCase)));

    public Task<Project?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_projects.FirstOrDefault(project => project.Id == id));

    private static Project Create(string name, string slug, bool isEnabled) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        Slug = slug,
        IsEnabled = isEnabled,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
