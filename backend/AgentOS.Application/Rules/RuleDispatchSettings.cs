using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentOS.Application.Rules;

/// <summary>
/// Dispatch parameters stored in <c>AutomationRules.DispatchJson</c>. References are resolved
/// to ids when the rule is saved, so this object only carries routing hints and limits.
/// </summary>
public sealed class RuleDispatchSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Suffix of the logical task key. Different dispatch keys let one rule fan out into more
    /// than one task for the same event while staying idempotent.
    /// </summary>
    public string? DispatchKey { get; init; }

    /// <summary>Task priority: low, normal, high or urgent.</summary>
    public string? Priority { get; init; }

    public static bool TryParse(string json, out RuleDispatchSettings settings, out string? error)
    {
        settings = new RuleDispatchSettings();
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RuleDispatchSettings>(json, Options);
            if (parsed is null)
            {
                error = "dispatchJson must be a JSON object.";
                return false;
            }

            settings = parsed;
            return true;
        }
        catch (JsonException exception)
        {
            error = $"dispatchJson is not a valid dispatch object: {exception.Message}";
            return false;
        }
    }

    public string ResolvedDispatchKey => string.IsNullOrWhiteSpace(DispatchKey) ? "default" : DispatchKey.Trim();

    public string ResolvedPriority => string.IsNullOrWhiteSpace(Priority) ? "normal" : Priority.Trim().ToLowerInvariant();
}
