using System.Text.RegularExpressions;

namespace AgentOS.Application.Rules;

/// <summary>
/// Glob matching for rule patterns. Only <c>*</c> is special (any run of characters); every
/// other character is literal. Matching is case-insensitive, mirroring the database collation.
/// A null, empty or <c>*</c> pattern matches everything.
/// </summary>
public static class RulePattern
{
    private const int MaxPatternLength = 120;

    public static bool IsValid(string? pattern, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        var trimmed = pattern.Trim();
        if (trimmed.Length > MaxPatternLength)
        {
            error = $"pattern must be {MaxPatternLength} characters or fewer.";
            return false;
        }

        if (trimmed.Contains('?', StringComparison.Ordinal))
        {
            error = "pattern only supports the '*' wildcard.";
            return false;
        }

        return true;
    }

    public static bool IsMatch(string? pattern, string? value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Trim() == "*")
        {
            return true;
        }

        var regex = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(
            value ?? string.Empty,
            regex,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(100));
    }
}
