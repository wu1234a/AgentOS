using System.Text.RegularExpressions;
using AgentOS.Domain.Agents;

namespace AgentOS.Application.Agents;

/// <summary>Agent registry operations: validation, CRUD and lifecycle transitions.</summary>
public sealed class AgentService(IAgentRepository repository, TimeProvider timeProvider)
{
    private static readonly Regex CodePattern = new(
        "^[a-z0-9][a-z0-9-]{2,79}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Roles = ["planner", "coder", "reviewer", "qa"];
    private static readonly HashSet<string> PermissionProfiles = ["read-only", "workspace-write"];

    public Task<(IReadOnlyList<AgentSummary> Items, int Total)> ListAsync(
        Guid? projectId,
        string? role,
        bool includeArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken)
        => repository.ListAsync(projectId, role, includeArchived, limit, offset, cancellationToken);

    public Task<AgentSummary?> GetAsync(Guid agentId, CancellationToken cancellationToken)
        => repository.GetSummaryAsync(agentId, cancellationToken);

    public async Task<AgentResult> CreateAsync(
        CreateAgentRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var code = NormalizeCode(request.Code, errors);

        var values = await NormalizeAsync(
            request.DisplayName,
            request.Role,
            request.Description,
            request.PermissionProfile,
            request.DefaultModelProfileId,
            request.SystemPromptPath,
            errors,
            cancellationToken);

        if (request.ProjectId is { } projectId
            && !await repository.ProjectEnabledAsync(projectId, cancellationToken))
        {
            errors["projectId"] = ["Project does not exist or is disabled."];
        }

        if (errors.Count > 0)
        {
            return AgentResult.Invalid(errors);
        }

        if (code is not null
            && await repository.CodeExistsAsync(request.ProjectId, code, null, cancellationToken))
        {
            return AgentResult.Conflict("agent-code-conflict");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var agent = new Agent
        {
            Id = Guid.CreateVersion7(),
            ProjectId = request.ProjectId,
            Code = code!,
            DisplayName = values.DisplayName,
            Role = values.Role,
            Description = values.Description,
            PermissionProfile = values.PermissionProfile,
            DefaultModelProfileId = values.ModelProfileId,
            SystemPromptPath = values.SystemPromptPath,
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (!await repository.TryAddAsync(agent, cancellationToken))
        {
            return AgentResult.Conflict("agent-code-conflict");
        }

        return AgentResult.Ok((await repository.GetSummaryAsync(agent.Id, cancellationToken))!);
    }

    public async Task<AgentResult> UpdateAsync(
        Guid agentId,
        UpdateAgentRequest request,
        CancellationToken cancellationToken)
    {
        var agent = await repository.GetTrackedAsync(agentId, cancellationToken);
        if (agent is null)
        {
            return AgentResult.NotFound();
        }

        if (agent.ArchivedAtUtc is not null)
        {
            return AgentResult.Conflict("agent-archived");
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var values = await NormalizeAsync(
            request.DisplayName,
            request.Role,
            request.Description,
            request.PermissionProfile,
            request.DefaultModelProfileId,
            request.SystemPromptPath,
            errors,
            cancellationToken);

        if (errors.Count > 0)
        {
            return AgentResult.Invalid(errors);
        }

        agent.DisplayName = values.DisplayName;
        agent.Role = values.Role;
        agent.Description = values.Description;
        agent.PermissionProfile = values.PermissionProfile;
        agent.DefaultModelProfileId = values.ModelProfileId;
        agent.SystemPromptPath = values.SystemPromptPath;
        agent.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await repository.SaveAsync(cancellationToken);
        return AgentResult.Ok((await repository.GetSummaryAsync(agentId, cancellationToken))!);
    }

    public Task<AgentResult> EnableAsync(Guid agentId, CancellationToken cancellationToken)
        => SetEnabledAsync(agentId, true, cancellationToken);

    public Task<AgentResult> DisableAsync(Guid agentId, CancellationToken cancellationToken)
        => SetEnabledAsync(agentId, false, cancellationToken);

    public async Task<AgentResult> ArchiveAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await repository.GetTrackedAsync(agentId, cancellationToken);
        if (agent is null)
        {
            return AgentResult.NotFound();
        }

        if (agent.ArchivedAtUtc is not null)
        {
            return AgentResult.Conflict("agent-already-archived");
        }

        var activeTasks = await repository.CountActiveTasksAsync(agentId, cancellationToken);
        if (activeTasks > 0)
        {
            return AgentResult.Conflict("agent-has-active-tasks");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        agent.IsEnabled = false;
        agent.ArchivedAtUtc = now;
        agent.UpdatedAtUtc = now;

        await repository.SaveAsync(cancellationToken);
        return AgentResult.Ok((await repository.GetSummaryAsync(agentId, cancellationToken))!);
    }

    private async Task<AgentResult> SetEnabledAsync(
        Guid agentId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var agent = await repository.GetTrackedAsync(agentId, cancellationToken);
        if (agent is null)
        {
            return AgentResult.NotFound();
        }

        if (agent.ArchivedAtUtc is not null)
        {
            return AgentResult.Conflict("agent-archived");
        }

        if (agent.IsEnabled != enabled)
        {
            agent.IsEnabled = enabled;
            agent.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await repository.SaveAsync(cancellationToken);
        }

        return AgentResult.Ok((await repository.GetSummaryAsync(agentId, cancellationToken))!);
    }

    private async Task<NormalizedAgent> NormalizeAsync(
        string? displayName,
        string? role,
        string? description,
        string? permissionProfile,
        Guid? modelProfileId,
        string? systemPromptPath,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        var normalizedName = displayName?.Trim() ?? string.Empty;
        if (normalizedName.Length is 0 or > 120)
        {
            errors["displayName"] = ["displayName is required and must be 120 characters or fewer."];
        }

        var normalizedRole = role?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Roles.Contains(normalizedRole))
        {
            errors["role"] = ["role must be one of: planner, coder, reviewer, qa."];
        }

        var normalizedPermission = permissionProfile?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!PermissionProfiles.Contains(normalizedPermission))
        {
            errors["permissionProfile"] = ["permissionProfile must be read-only or workspace-write."];
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription is { Length: > 1000 })
        {
            errors["description"] = ["description must be 1000 characters or fewer."];
        }

        var normalizedPromptPath = string.IsNullOrWhiteSpace(systemPromptPath)
            ? null
            : systemPromptPath.Trim();

        if (normalizedPromptPath is not null)
        {
            if (normalizedPromptPath.Length > 500)
            {
                errors["systemPromptPath"] = ["systemPromptPath must be 500 characters or fewer."];
            }
            else if (Path.IsPathRooted(normalizedPromptPath)
                     || normalizedPromptPath.StartsWith('/')
                     || normalizedPromptPath.StartsWith('\\')
                     || normalizedPromptPath.Contains(':'))
            {
                errors["systemPromptPath"] = ["systemPromptPath must be a relative path."];
            }
            else if (normalizedPromptPath.Split('/', '\\').Any(segment => segment == ".."))
            {
                errors["systemPromptPath"] = ["systemPromptPath must not contain '..'."];
            }
        }

        if (modelProfileId is { } profileId
            && !await repository.ModelProfileEnabledAsync(profileId, cancellationToken))
        {
            errors["defaultModelProfileId"] = ["Model profile does not exist or is disabled."];
        }

        return new NormalizedAgent(
            normalizedName,
            normalizedRole,
            normalizedDescription,
            normalizedPermission,
            modelProfileId,
            normalizedPromptPath);
    }

    private static string? NormalizeCode(string? code, Dictionary<string, string[]> errors)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !CodePattern.IsMatch(normalized))
        {
            errors["code"] = ["code must be a lowercase slug of 3-80 characters."];
            return null;
        }

        return normalized;
    }

    private sealed record NormalizedAgent(
        string DisplayName,
        string Role,
        string? Description,
        string PermissionProfile,
        Guid? ModelProfileId,
        string? SystemPromptPath);
}
