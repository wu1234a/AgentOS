using System.Text.RegularExpressions;

namespace AgentOS.Application.Observability;

/// <summary>
/// Server-side redaction for runner-supplied log lines. The runner redacts first; this is the
/// defence-in-depth pass before anything is persisted or shown in the UI.
/// </summary>
public static class TaskLogSanitizer
{
    private static readonly Regex[] Patterns =
    [
        new(@"sk-[A-Za-z0-9_\-]{8,}", RegexOptions.Compiled),
        new(@"(?i)bearer\s+[A-Za-z0-9._\-]+", RegexOptions.Compiled),
        new(@"(?i)(password|pwd|api[_-]?key|access[_-]?token|client[_-]?secret|secret)\s*[:=]\s*[^\s,;]+", RegexOptions.Compiled),
        new(@"(?i)cookie\s*[:=]\s*[^\s,;]+", RegexOptions.Compiled)
    ];

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = text;
        foreach (var pattern in Patterns)
        {
            result = pattern.Replace(result, "[REDACTED]");
        }

        return result;
    }
}
