using System.Text.Json;
using AgentOS.Domain.Rules;

namespace AgentOS.Application.Rules;

/// <summary>Rule management use cases: create, update, enable/disable and validate.</summary>
public sealed class RuleService(
    RuleValidator validator,
    IAutomationRuleRepository repository,
    TimeProvider timeProvider)
{
    public Task<RulePage> ListAsync(RuleListQuery query, CancellationToken cancellationToken)
        => repository.ListAsync(query, cancellationToken);

    public Task<RuleDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
        => repository.GetAsync(id, cancellationToken);

    /// <summary>Creates a rule. Returns null when validation fails (errors are in the result).</summary>
    public async Task<RuleMutationResult> CreateAsync(
        SaveRuleRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(
            request,
            requireEnabledTargets: request.IsEnabled ?? false,
            cancellationToken);

        if (!validation.IsValid)
        {
            return RuleMutationResult.Invalid(validation.Errors);
        }

        var values = validation.Values!;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rule = new AutomationRule
        {
            Id = Guid.CreateVersion7(),
            Name = values.Name,
            ProjectId = values.ProjectId,
            SourcePattern = values.SourcePattern,
            EventTypePattern = values.EventTypePattern,
            MatchJson = values.MatchJson,
            AgentId = values.AgentId,
            SkillId = values.SkillId,
            ModelProfileId = values.ModelProfileId,
            DispatchJson = values.DispatchJson,
            Priority = values.Priority,
            MaxParallel = values.MaxParallel,
            MaxAttempts = values.MaxAttempts,
            TimeoutSeconds = values.TimeoutSeconds,
            NotificationPolicy = values.NotificationPolicy,
            IsEnabled = values.IsEnabled,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await repository.AddAsync(rule, cancellationToken);
        var detail = await repository.GetAsync(rule.Id, cancellationToken);
        return RuleMutationResult.Success(detail!);
    }

    /// <summary>Updates a rule. Returns null when the rule does not exist.</summary>
    public async Task<RuleMutationResult?> UpdateAsync(
        Guid id,
        SaveRuleRequest request,
        CancellationToken cancellationToken)
    {
        var rule = await repository.FindEntityAsync(id, cancellationToken);
        if (rule is null)
        {
            return null;
        }

        var validation = await validator.ValidateAsync(
            request,
            requireEnabledTargets: request.IsEnabled ?? false,
            cancellationToken);

        if (!validation.IsValid)
        {
            return RuleMutationResult.Invalid(validation.Errors);
        }

        var values = validation.Values!;
        rule.Name = values.Name;
        rule.ProjectId = values.ProjectId;
        rule.SourcePattern = values.SourcePattern;
        rule.EventTypePattern = values.EventTypePattern;
        rule.MatchJson = values.MatchJson;
        rule.AgentId = values.AgentId;
        rule.SkillId = values.SkillId;
        rule.ModelProfileId = values.ModelProfileId;
        rule.DispatchJson = values.DispatchJson;
        rule.Priority = values.Priority;
        rule.MaxParallel = values.MaxParallel;
        rule.MaxAttempts = values.MaxAttempts;
        rule.TimeoutSeconds = values.TimeoutSeconds;
        rule.NotificationPolicy = values.NotificationPolicy;
        rule.IsEnabled = values.IsEnabled;
        rule.Version += 1;
        rule.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await repository.SaveAsync(cancellationToken);
        var detail = await repository.GetAsync(id, cancellationToken);
        return RuleMutationResult.Success(detail!);
    }

    /// <summary>
    /// Enables or disables a rule. Enabling re-validates the stored rule against currently
    /// enabled targets, so a rule cannot be switched on while its agent/skill/profile is off.
    /// Returns null when the rule does not exist.
    /// </summary>
    public async Task<RuleMutationResult?> SetEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var detail = await repository.GetAsync(id, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        if (isEnabled)
        {
            var request = ToSaveRequest(detail, isEnabled: true);
            var validation = await validator.ValidateAsync(
                request,
                requireEnabledTargets: true,
                cancellationToken);

            if (!validation.IsValid)
            {
                return RuleMutationResult.Invalid(validation.Errors);
            }
        }

        var rule = await repository.FindEntityAsync(id, cancellationToken);
        if (rule is null)
        {
            return null;
        }

        rule.IsEnabled = isEnabled;
        rule.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await repository.SaveAsync(cancellationToken);

        var updated = await repository.GetAsync(id, cancellationToken);
        return RuleMutationResult.Success(updated!);
    }

    /// <summary>Validates a payload without saving it.</summary>
    public async Task<RuleValidationResponse> ValidateAsync(
        SaveRuleRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(
            request,
            requireEnabledTargets: request.IsEnabled ?? false,
            cancellationToken);

        if (!validation.IsValid)
        {
            return new RuleValidationResponse(false, validation.Errors, null);
        }

        var values = validation.Values!;
        return new RuleValidationResponse(
            true,
            validation.Errors,
            new NormalizedRule(
                values.Name,
                values.ProjectSlug,
                values.SourcePattern,
                values.EventTypePattern,
                values.AgentCode,
                values.SkillCode,
                values.ModelProfileCode,
                JsonDocument.Parse(values.MatchJson).RootElement.Clone(),
                JsonDocument.Parse(values.DispatchJson).RootElement.Clone(),
                values.Priority,
                values.MaxParallel,
                values.MaxAttempts,
                values.TimeoutSeconds,
                values.NotificationPolicy,
                values.IsEnabled));
    }

    private static SaveRuleRequest ToSaveRequest(RuleDetail detail, bool isEnabled) => new()
    {
        Name = detail.Name,
        Project = detail.ProjectSlug,
        SourcePattern = detail.SourcePattern,
        EventTypePattern = detail.EventTypePattern,
        AgentCode = detail.AgentCode,
        SkillCode = detail.SkillCode,
        ModelProfileCode = detail.ModelProfileCode,
        MatchJson = detail.MatchJson,
        DispatchJson = detail.DispatchJson,
        Priority = detail.Priority,
        MaxParallel = detail.MaxParallel,
        MaxAttempts = detail.MaxAttempts,
        TimeoutSeconds = detail.TimeoutSeconds,
        NotificationPolicy = detail.NotificationPolicy,
        IsEnabled = isEnabled
    };
}
