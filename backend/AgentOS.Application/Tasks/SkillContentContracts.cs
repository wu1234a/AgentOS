namespace AgentOS.Application.Tasks;

/// <summary>Where skill markdown lives on the control plane.</summary>
public sealed class SkillContentOptions
{
    public string? RootPath { get; set; }

    public int MaxBytes { get; set; } = 65_536;
}

/// <summary>Resolves a Skill's relative path to its content without allowing path escapes.</summary>
public interface ISkillContentReader
{
    Task<string?> ReadAsync(string? relativePath, CancellationToken cancellationToken);
}
