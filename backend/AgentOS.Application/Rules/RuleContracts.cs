using System.Text.Json;

namespace AgentOS.Application.Rules;

/// <summary>Create/update payload for a rule. Optional properties are validated explicitly.</summary>
public sealed class SaveRuleRequest
{
    public string? Name { get; init; }

    /// <summary>Project slug; null keeps the rule global.</summary>
    public string? Project { get; init; }

    public string? SourcePattern { get; init; }

    public string? EventTypePattern { get; init; }

    /// <summary>Agent code; resolved to AgentId on save.</summary>
    public string? AgentCode { get; init; }

    public string? SkillCode { get; init; }

    public string? ModelProfileCode { get; init; }

    /// <summary>Optional structured conditions (subjectTypes, priorities).</summary>
    public JsonElement? MatchJson { get; init; }

    /// <summary>Optional dispatch settings (dispatchKey, priority).</summary>
    public JsonElement? DispatchJson { get; init; }

    public int? Priority { get; init; }

    public int? MaxParallel { get; init; }

    public int? MaxAttempts { get; init; }

    public int? TimeoutSeconds { get; init; }

    public string? NotificationPolicy { get; init; }

    public bool? IsEnabled { get; init; }
}

/// <summary>Row shown in the rule list.</summary>
public sealed record RuleSummary(
    Guid Id,
    string Name,
    string? ProjectSlug,
    string SourcePattern,
    string EventTypePattern,
    string AgentCode,
    string? SkillCode,
    string? ModelProfileCode,
    int Priority,
    int MaxParallel,
    int MaxAttempts,
    int TimeoutSeconds,
    string NotificationPolicy,
    bool IsEnabled,
    int Version,
    DateTime UpdatedAtUtc);

/// <summary>Full rule representation, including the raw JSON documents.</summary>
public sealed record RuleDetail(
    Guid Id,
    string Name,
    Guid? ProjectId,
    string? ProjectSlug,
    string SourcePattern,
    string EventTypePattern,
    string AgentCode,
    Guid AgentId,
    string? SkillCode,
    Guid? SkillId,
    string? ModelProfileCode,
    Guid? ModelProfileId,
    JsonElement MatchJson,
    JsonElement DispatchJson,
    int Priority,
    int MaxParallel,
    int MaxAttempts,
    int TimeoutSeconds,
    string NotificationPolicy,
    bool IsEnabled,
    int Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record RulePage(IReadOnlyList<RuleSummary> Items, int Page, int PageSize, int Total);

/// <summary>Either the saved rule or field-level validation errors.</summary>
public sealed record RuleMutationResult(RuleDetail? Rule, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsSuccess => Rule is not null;

    public static RuleMutationResult Success(RuleDetail rule) => new(rule, EmptyErrors);

    public static RuleMutationResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);

    private static readonly Dictionary<string, string[]> EmptyErrors = new(StringComparer.Ordinal);
}

/// <summary>Result of <c>POST /api/v1/rules/validate</c>: errors plus the normalized rule preview.</summary>
public sealed record RuleValidationResponse(
    bool IsValid,
    IReadOnlyDictionary<string, string[]> Errors,
    NormalizedRule? Normalized);

public sealed record NormalizedRule(
    string Name,
    string? ProjectSlug,
    string SourcePattern,
    string EventTypePattern,
    string AgentCode,
    string? SkillCode,
    string? ModelProfileCode,
    JsonElement MatchJson,
    JsonElement DispatchJson,
    int Priority,
    int MaxParallel,
    int MaxAttempts,
    int TimeoutSeconds,
    string NotificationPolicy,
    bool IsEnabled);
