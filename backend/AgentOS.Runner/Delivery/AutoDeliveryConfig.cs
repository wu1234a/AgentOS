using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentOS.Runner.Delivery;

/// <summary>
/// Optional delivery block embedded in a task's InputJson. Delivery is opt-in so existing tasks
/// keep their current "edit the workspace only" behaviour.
/// </summary>
public sealed partial record AutoDeliveryConfig(
    string Repository,
    string BaseBranch,
    string Branch,
    string CommitMessage,
    string PullRequestTitle,
    string PullRequestBody,
    string Remote,
    string TokenEnvironmentVariable,
    bool RequirePullRequest)
{
    private const int MaxTextLength = 4000;

    public static bool TryParse(
        string inputJson,
        string taskCode,
        out AutoDeliveryConfig? config,
        out string? error)
    {
        config = null;
        error = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            error = "Task input is not valid JSON.";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("delivery", out var delivery)
                || delivery.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (delivery.ValueKind != JsonValueKind.Object)
            {
                error = "delivery must be a JSON object.";
                return false;
            }

            var enabled = ReadBoolean(delivery, "enabled", true);
            if (!enabled)
            {
                return true;
            }

            var repository = ReadString(delivery, "repository")?.Trim() ?? string.Empty;
            var baseBranch = ReadString(delivery, "baseBranch")?.Trim() ?? "main";
            var branch = ReadString(delivery, "branch")?.Trim();
            var commitMessage = ReadString(delivery, "commitMessage")?.Trim();
            var title = ReadString(delivery, "title")?.Trim();
            var body = ReadString(delivery, "body")?.Trim();
            var remote = ReadString(delivery, "remote")?.Trim() ?? "origin";
            var tokenVariable = ReadString(delivery, "tokenEnvironmentVariable")?.Trim()
                ?? "AGENTOS_GITHUB_TOKEN";
            var requirePullRequest = ReadBoolean(delivery, "requirePullRequest", true);

            branch ??= $"aos/{Slug(taskCode)}";
            commitMessage ??= $"chore: apply {taskCode}";
            title ??= commitMessage;
            body ??= $"Automated by AgentOS task {taskCode}.";

            if (!IsValidRepository(repository))
            {
                error = "delivery.repository must be owner/repo.";
                return false;
            }

            if (!IsValidBranch(baseBranch))
            {
                error = "delivery.baseBranch is not a valid git branch name.";
                return false;
            }

            if (!IsValidBranch(branch))
            {
                error = "delivery.branch is not a valid git branch name.";
                return false;
            }

            if (string.Equals(branch, baseBranch, StringComparison.Ordinal))
            {
                error = "delivery.branch must differ from delivery.baseBranch.";
                return false;
            }

            if (!IsValidBranch(remote))
            {
                error = "delivery.remote is not a valid git remote name.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(commitMessage) || commitMessage.Length > 200)
            {
                error = "delivery.commitMessage must be between 1 and 200 characters.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            {
                error = "delivery.title must be between 1 and 200 characters.";
                return false;
            }

            if (body.Length > MaxTextLength)
            {
                error = $"delivery.body must be at most {MaxTextLength} characters.";
                return false;
            }

            if (!TokenVariableRegex().IsMatch(tokenVariable))
            {
                error = "delivery.tokenEnvironmentVariable must be an environment variable name.";
                return false;
            }

            if (requirePullRequest && string.IsNullOrWhiteSpace(repository))
            {
                error = "delivery.repository is required when requirePullRequest is true.";
                return false;
            }

            config = new AutoDeliveryConfig(
                repository,
                baseBranch,
                branch,
                commitMessage,
                title,
                body,
                remote,
                tokenVariable,
                requirePullRequest);
            return true;
        }
    }

    internal static bool IsValidBranch(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 120
            || value.StartsWith('-')
            || value.EndsWith('/')
            || value.EndsWith('.')
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains("@{", StringComparison.Ordinal)
            || value.Contains("//", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains(' ')
            || value.Contains('~')
            || value.Contains('^')
            || value.Contains(':')
            || value.Contains('?')
            || value.Contains('*')
            || value.Contains('[')
            || value.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character is not ('-' or '_' or '.' or '/'))
            {
                return false;
            }
        }

        return value.Split('/').All(part => part.Length > 0);
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string name, bool fallback)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static bool IsValidRepository(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
        {
            return false;
        }

        var parts = value.Split('/');
        return parts.Length == 2
            && parts.All(part => part.Length > 0
                && part.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }

    private static string Slug(string value)
    {
        var characters = value
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = new string(characters).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "task" : slug;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]{2,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenVariableRegex();
}
