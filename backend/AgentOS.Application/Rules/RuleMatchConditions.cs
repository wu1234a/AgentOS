using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentOS.Application.Rules;

/// <summary>
/// Optional structured predicates stored in <c>AutomationRules.MatchJson</c>. The schema is
/// deliberately small and declarative: rules never execute code, and unknown properties are
/// rejected so a typo cannot silently widen a rule.
/// </summary>
public sealed class RuleMatchConditions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Event subject types the rule accepts; empty means any.</summary>
    public string[] SubjectTypes { get; init; } = [];

    /// <summary>Event priorities the rule accepts; empty means any.</summary>
    public string[] Priorities { get; init; } = [];

    public static bool TryParse(string json, out RuleMatchConditions conditions, out string? error)
    {
        conditions = new RuleMatchConditions();
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RuleMatchConditions>(json, Options);
            if (parsed is null)
            {
                error = "matchJson must be a JSON object.";
                return false;
            }

            // System.Text.Json sets a missing or explicit null array to null even though the
            // property has an initializer, so normalize before validating.
            var subjectTypes = parsed.SubjectTypes ?? [];
            var priorities = parsed.Priorities ?? [];

            if (subjectTypes.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 64))
            {
                error = "matchJson.subjectTypes entries must be 1-64 characters.";
                return false;
            }

            if (priorities.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 16))
            {
                error = "matchJson.priorities entries must be 1-16 characters.";
                return false;
            }

            conditions = new RuleMatchConditions
            {
                SubjectTypes = subjectTypes,
                Priorities = priorities
            };
            return true;
        }
        catch (JsonException)
        {
            error = "matchJson must be a JSON object with only subjectTypes and priorities.";
            return false;
        }
    }

    public bool Matches(string? subjectType, string priority)
    {
        if (SubjectTypes.Length > 0
            && !SubjectTypes.Contains(subjectType ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return Priorities.Length == 0
               || Priorities.Contains(priority, StringComparer.OrdinalIgnoreCase);
    }
}
