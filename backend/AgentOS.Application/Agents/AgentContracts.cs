using AgentOS.Domain.Agents;

namespace AgentOS.Application.Agents;

public sealed record AgentSummary(
    Guid Id,
    Guid? ProjectId,
    string? ProjectSlug,
    string Code,
    string DisplayName,
    string Role,
    string? Description,
    string PermissionProfile,
    Guid? DefaultModelProfileId,
    string? SystemPromptPath,
    bool IsEnabled,
    DateTime? ArchivedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class CreateAgentRequest
{
    public Guid? ProjectId { get; init; }

    public string? Code { get; init; }

    public string? DisplayName { get; init; }

    public string? Role { get; init; }

    public string? Description { get; init; }

    public string? PermissionProfile { get; init; }

    public Guid? DefaultModelProfileId { get; init; }

    public string? SystemPromptPath { get; init; }
}

public sealed class UpdateAgentRequest
{
    public string? DisplayName { get; init; }

    public string? Role { get; init; }

    public string? Description { get; init; }

    public string? PermissionProfile { get; init; }

    public Guid? DefaultModelProfileId { get; init; }

    public string? SystemPromptPath { get; init; }
}

public enum AgentOutcome
{
    Ok = 0,
    NotFound = 1,
    Conflict = 2,
    ValidationFailed = 3
}

public sealed record AgentResult(
    AgentOutcome Outcome,
    AgentSummary? Agent,
    string? ErrorCode,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static AgentResult Ok(AgentSummary agent) => new(AgentOutcome.Ok, agent, null, Empty);

    public static AgentResult NotFound() => new(AgentOutcome.NotFound, null, "agent-not-found", Empty);

    public static AgentResult Conflict(string code) => new(AgentOutcome.Conflict, null, code, Empty);

    public static AgentResult Invalid(IReadOnlyDictionary<string, string[]> errors)
        => new(AgentOutcome.ValidationFailed, null, "agent-invalid", errors);

    private static readonly Dictionary<string, string[]> Empty = [];
}

/// <summary>Persistence seam for the agent registry.</summary>
public interface IAgentRepository
{
    Task<(IReadOnlyList<AgentSummary> Items, int Total)> ListAsync(
        Guid? projectId,
        string? role,
        bool includeArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<AgentSummary?> GetSummaryAsync(Guid agentId, CancellationToken cancellationToken);

    Task<Agent?> GetTrackedAsync(Guid agentId, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(
        Guid? projectId,
        string code,
        Guid? excludeAgentId,
        CancellationToken cancellationToken);

    Task<bool> ProjectEnabledAsync(Guid projectId, CancellationToken cancellationToken);

    Task<bool> ModelProfileEnabledAsync(Guid modelProfileId, CancellationToken cancellationToken);

    Task<int> CountActiveTasksAsync(Guid agentId, CancellationToken cancellationToken);

    Task<bool> TryAddAsync(Agent agent, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
