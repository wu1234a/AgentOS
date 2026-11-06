namespace AgentOS.Domain.Tasks;

/// <summary>
/// Identity of the logical task produced by a rule dispatch.
/// </summary>
/// <remarks>
/// AOS-004 turns events into tasks, and the same event may be delivered more than once
/// (retries, replays) while the rules engine restarts mid-flight. The idempotency key for
/// task creation is therefore <c>EventId + RuleId + RuleVersion + DispatchKey</c>
/// (see <c>docs/EVENT-SPECIFICATION.md</c> §8 and <c>docs/DATABASE-ER.md</c> §3.7
/// <c>Tasks.LogicalTaskKey</c>). This helper is the single place that composes the key so
/// the persistence layer and the rules engine cannot drift apart.
/// </remarks>
public static class LogicalTaskKey
{
    /// <summary>Maximum stored length of <c>Tasks.LogicalTaskKey</c>.</summary>
    public const int MaxLength = 400;

    /// <summary>Maximum accepted length of the caller-supplied dispatch key.</summary>
    public const int MaxDispatchKeyLength = 200;

    private const char Separator = ':';

    /// <summary>
    /// Builds the idempotency key for one rule dispatch of one event.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="dispatchKey"/> is empty, too long, or contains the key separator.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ruleVersion"/> is below 1.</exception>
    public static string Create(Guid eventId, Guid ruleId, int ruleVersion, string dispatchKey)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("eventId must not be empty.", nameof(eventId));
        }

        if (ruleId == Guid.Empty)
        {
            throw new ArgumentException("ruleId must not be empty.", nameof(ruleId));
        }

        if (ruleVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ruleVersion), ruleVersion, "ruleVersion must be 1 or greater.");
        }

        var normalizedDispatchKey = NormalizeDispatchKey(dispatchKey);

        return string.Create(
            null,
            $"{eventId:N}{Separator}{ruleId:N}{Separator}{ruleVersion}{Separator}{normalizedDispatchKey}");
    }

    /// <summary>Validates a caller-supplied dispatch key.</summary>
    public static bool IsValidDispatchKey(string? dispatchKey, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(dispatchKey))
        {
            return false;
        }

        var trimmed = dispatchKey.Trim();
        if (trimmed.Length > MaxDispatchKeyLength || trimmed.Contains(Separator, StringComparison.Ordinal))
        {
            return false;
        }

        normalized = trimmed;
        return true;
    }

    private static string NormalizeDispatchKey(string dispatchKey)
        => IsValidDispatchKey(dispatchKey, out var normalized)
            ? normalized
            : throw new ArgumentException(
                $"dispatchKey must be 1-{MaxDispatchKeyLength} characters and must not contain '{Separator}'.",
                nameof(dispatchKey));
}
