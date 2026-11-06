using System.Text.RegularExpressions;

namespace AgentOS.AgentAdapters;

/// <summary>Removes credentials from captured output before it is stored or logged.</summary>
public static class LogRedactor
{
    private static readonly Regex[] Patterns =
    [
        new(@"sk-[A-Za-z0-9_\-]{8,}", RegexOptions.Compiled),
        new(@"(?i)bearer\s+[A-Za-z0-9._\-]+", RegexOptions.Compiled),
        new(@"(?i)(api[_-]?key|access[_-]?token|client[_-]?secret|secret)\s*[:=]\s*[^\s,;]+", RegexOptions.Compiled)
    ];

    public static string Redact(string? text, IEnumerable<string>? secrets = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = text;
        foreach (var secret in secrets ?? [])
        {
            if (!string.IsNullOrWhiteSpace(secret) && secret.Length >= 6)
            {
                result = result.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            }
        }

        foreach (var pattern in Patterns)
        {
            result = pattern.Replace(result, "[REDACTED]");
        }

        return result;
    }
}
