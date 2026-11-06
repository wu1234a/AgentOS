using System.Text.Json;

namespace AgentOS.Application.Rules;

/// <summary>Normalized values plus the resolved reference ids.</summary>
public sealed record NormalizedRuleValues(
    string Name,
    Guid? ProjectId,
    string? ProjectSlug,
    string SourcePattern,
    string EventTypePattern,
    Guid AgentId,
    string AgentCode,
    Guid? SkillId,
    string? SkillCode,
    Guid? ModelProfileId,
    string? ModelProfileCode,
    string MatchJson,
    string DispatchJson,
    int Priority,
    int MaxParallel,
    int MaxAttempts,
    int TimeoutSeconds,
    string NotificationPolicy,
    bool IsEnabled);

/// <summary>Validation outcome: field errors and, when valid, the normalized rule.</summary>
public sealed record RuleValidationResult(
    IReadOnlyDictionary<string, string[]> Errors,
    NormalizedRuleValues? Values)
{
    public bool IsValid => Values is not null && Errors.Count == 0;
}

/// <summary>
/// Validates a rule payload and resolves its references. Validation is shared by create,
/// update, validate-only and enable so the same invariants always apply.
/// </summary>
public sealed class RuleValidator(IRuleReferenceLookup lookup)
{
    private static readonly HashSet<string> Priorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "low", "normal", "high", "urgent"
    };

    private static readonly HashSet<string> NotificationPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "failure", "failure-or-approval", "always"
    };

    public async Task<RuleValidationResult> ValidateAsync(
        SaveRuleRequest request,
        bool requireEnabledTargets,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var messages))
            {
                messages = [];
                errors[field] = messages;
            }

            messages.Add(message);
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Add("name", "name is required.");
        }
        else if (name.Length > 120)
        {
            Add("name", "name must be 120 characters or fewer.");
        }

        // Project scope.
        Guid? projectId = null;
        string? projectSlug = null;
        if (!string.IsNullOrWhiteSpace(request.Project))
        {
            projectSlug = request.Project.Trim();
            var project = await lookup.FindProjectAsync(projectSlug, cancellationToken);
            if (project is null)
            {
                Add("project", $"Project '{projectSlug}' was not found.");
                projectSlug = null;
            }
            else if (!project.IsEnabled)
            {
                Add("project", $"Project '{projectSlug}' is disabled.");
            }
            else
            {
                projectId = project.Id;
            }
        }

        var sourcePattern = string.IsNullOrWhiteSpace(request.SourcePattern) ? "*" : request.SourcePattern.Trim();
        if (sourcePattern.Length > 64)
        {
            // The column is nvarchar(64); rejecting here keeps a 400 instead of a DB truncation 500.
            Add("sourcePattern", "sourcePattern must be 64 characters or fewer.");
        }
        else if (!RulePattern.IsValid(sourcePattern, out var sourceError))
        {
            Add("sourcePattern", sourceError!);
        }

        var eventTypePattern = string.IsNullOrWhiteSpace(request.EventTypePattern)
            ? "*"
            : request.EventTypePattern.Trim();
        if (!RulePattern.IsValid(eventTypePattern, out var eventTypeError))
        {
            Add("eventTypePattern", eventTypeError!);
        }

        // Agent (required).
        Guid agentId = Guid.Empty;
        var agentCode = request.AgentCode?.Trim();
        if (string.IsNullOrWhiteSpace(agentCode))
        {
            Add("agentCode", "agentCode is required.");
        }
        else
        {
            var agent = await lookup.FindAgentAsync(agentCode, projectId, cancellationToken);
            if (agent is null)
            {
                Add("agentCode", $"Agent '{agentCode}' was not found for this project.");
            }
            else
            {
                if (requireEnabledTargets && !agent.IsEnabled)
                {
                    Add("agentCode", $"Agent '{agentCode}' is disabled; the rule cannot be enabled.");
                }

                if (agent.ProjectId is not null && projectId is not null && agent.ProjectId != projectId)
                {
                    Add("agentCode", $"Agent '{agentCode}' belongs to another project.");
                }

                agentId = agent.Id;
            }
        }

        // Optional skill.
        Guid? skillId = null;
        var skillCode = request.SkillCode?.Trim();
        if (!string.IsNullOrWhiteSpace(skillCode))
        {
            var skill = await lookup.FindSkillAsync(skillCode, projectId, cancellationToken);
            if (skill is null)
            {
                Add("skillCode", $"Skill '{skillCode}' was not found for this project.");
            }
            else
            {
                if (requireEnabledTargets && !skill.IsEnabled)
                {
                    Add("skillCode", $"Skill '{skillCode}' is disabled; the rule cannot be enabled.");
                }

                if (skill.ProjectId is not null && projectId is not null && skill.ProjectId != projectId)
                {
                    Add("skillCode", $"Skill '{skillCode}' belongs to another project.");
                }

                skillId = skill.Id;
            }
        }

        // Optional model profile.
        Guid? modelProfileId = null;
        var modelProfileCode = request.ModelProfileCode?.Trim();
        if (!string.IsNullOrWhiteSpace(modelProfileCode))
        {
            var profile = await lookup.FindModelProfileAsync(modelProfileCode, cancellationToken);
            if (profile is null)
            {
                Add("modelProfileCode", $"Model profile '{modelProfileCode}' was not found.");
            }
            else
            {
                if (requireEnabledTargets && !profile.IsEnabled)
                {
                    Add("modelProfileCode", $"Model profile '{modelProfileCode}' is disabled; the rule cannot be enabled.");
                }

                modelProfileId = profile.Id;
            }
        }

        // Structured JSON documents.
        var matchJson = "{}";
        if (request.MatchJson is { } matchElement && matchElement.ValueKind != JsonValueKind.Null)
        {
            if (matchElement.ValueKind != JsonValueKind.Object)
            {
                Add("matchJson", "matchJson must be a JSON object.");
            }
            else if (!RuleMatchConditions.TryParse(matchElement.GetRawText(), out _, out var matchError))
            {
                Add("matchJson", matchError!);
            }
            else
            {
                matchJson = matchElement.GetRawText();
            }
        }

        var dispatchJson = "{}";
        var dispatchKey = "default";
        var taskPriority = "normal";
        if (request.DispatchJson is { } dispatchElement && dispatchElement.ValueKind != JsonValueKind.Null)
        {
            if (dispatchElement.ValueKind != JsonValueKind.Object)
            {
                Add("dispatchJson", "dispatchJson must be a JSON object.");
            }
            else if (!RuleDispatchSettings.TryParse(dispatchElement.GetRawText(), out var settings, out var dispatchError))
            {
                Add("dispatchJson", dispatchError!);
            }
            else
            {
                dispatchKey = settings.ResolvedDispatchKey;
                taskPriority = settings.ResolvedPriority;
                if (!Domain.Tasks.LogicalTaskKey.IsValidDispatchKey(dispatchKey, out _))
                {
                    Add("dispatchJson",
                        $"dispatchJson.dispatchKey must be 1-200 characters and must not contain ':'.");
                }

                if (!Priorities.Contains(taskPriority))
                {
                    Add("dispatchJson", "dispatchJson.priority must be one of low, normal, high or urgent.");
                }

                dispatchJson = dispatchElement.GetRawText();
            }
        }

        var priority = request.Priority ?? 0;
        if (priority is < -1000 or > 1000)
        {
            Add("priority", "priority must be between -1000 and 1000.");
        }

        var maxParallel = request.MaxParallel ?? 1;
        if (maxParallel is < 1 or > 64)
        {
            Add("maxParallel", "maxParallel must be between 1 and 64.");
        }

        var maxAttempts = request.MaxAttempts ?? 2;
        if (maxAttempts is < 1 or > 10)
        {
            Add("maxAttempts", "maxAttempts must be between 1 and 10.");
        }

        var timeoutSeconds = request.TimeoutSeconds ?? 1800;
        if (timeoutSeconds is < 10 or > 86400)
        {
            Add("timeoutSeconds", "timeoutSeconds must be between 10 and 86400.");
        }

        var notificationPolicy = string.IsNullOrWhiteSpace(request.NotificationPolicy)
            ? "none"
            : request.NotificationPolicy.Trim().ToLowerInvariant();
        if (!NotificationPolicies.Contains(notificationPolicy))
        {
            Add("notificationPolicy", "notificationPolicy must be one of none, failure, failure-or-approval or always.");
        }

        var fieldErrors = errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);

        if (fieldErrors.Count > 0)
        {
            return new RuleValidationResult(fieldErrors, null);
        }

        var values = new NormalizedRuleValues(
            name!,
            projectId,
            projectSlug,
            sourcePattern,
            eventTypePattern,
            agentId,
            agentCode!,
            skillId,
            skillCode,
            modelProfileId,
            modelProfileCode,
            matchJson,
            dispatchJson,
            priority,
            maxParallel,
            maxAttempts,
            timeoutSeconds,
            notificationPolicy,
            request.IsEnabled ?? false);

        return new RuleValidationResult(fieldErrors, values);
    }
}
