using AgentOS.Application.Tasks;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>Skill reader stub; returns no content unless a path is registered.</summary>
public sealed class FakeSkillContentReader : ISkillContentReader
{
    public Dictionary<string, string> Contents { get; } = [];

    public Task<string?> ReadAsync(string? relativePath, CancellationToken cancellationToken)
        => Task.FromResult(
            relativePath is not null && Contents.TryGetValue(relativePath, out var content)
                ? content
                : null);
}
