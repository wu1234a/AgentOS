using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentOS.Api.GitHub;

public sealed record GitHubWorkflowRun(long Id, string Name, string Status, string? Conclusion, int RunAttempt);

public interface IGitHubCommitStatusClient
{
    Task<bool> PostAsync(
        string repository,
        string sha,
        string state,
        string description,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GitHubWorkflowRun>?> GetWorkflowRunsAsync(
        string repository,
        string sha,
        CancellationToken cancellationToken);

    Task<(string Status, string? Reason)> GetBranchProtectionAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken);
}

public sealed class GitHubCommitStatusClient(
    HttpClient httpClient,
    Microsoft.Extensions.Options.IOptions<GitHubOptions> options) : IGitHubCommitStatusClient
{
    public async Task<bool> PostAsync(
        string repository,
        string sha,
        string state,
        string description,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.github.com/repos/{repository}/statuses/{sha}");
        request.Headers.UserAgent.ParseAdd("AgentOS/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(options.Value.ReviewToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ReviewToken);
        }

        var text = description.Length <= 140 ? description : description[..140];
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { state, context = "agentos/quality-gate", description = text }),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<GitHubWorkflowRun>?> GetWorkflowRunsAsync(
        string repository,
        string sha,
        CancellationToken cancellationToken)
    {
        var runs = new List<GitHubWorkflowRun>();
        for (var page = 1; page <= 10; page++)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repository}/actions/runs?head_sha={sha}&per_page=100&page={page}");
            AddHeaders(request);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("workflow_runs", out var workflowRuns)
                || workflowRuns.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var run in workflowRuns.EnumerateArray())
            {
                runs.Add(new GitHubWorkflowRun(
                    run.GetProperty("id").GetInt64(),
                    run.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    run.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty,
                    run.TryGetProperty("conclusion", out var conclusion) && conclusion.ValueKind == JsonValueKind.String
                        ? conclusion.GetString()
                        : null,
                    run.TryGetProperty("run_attempt", out var attempt) ? attempt.GetInt32() : 1));
            }

            if (workflowRuns.GetArrayLength() < 100)
            {
                break;
            }
        }

        return runs;
    }

    public async Task<(string Status, string? Reason)> GetBranchProtectionAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository}/branches/{branch}/protection");
        AddHeaders(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ("not_protected", "Branch has no classic protection rule.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return ("unverified", $"GitHub returned HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("required_status_checks", out var checks)
            || checks.ValueKind != JsonValueKind.Object)
        {
            return ("unverified", "required_status_checks is not an object.");
        }

        var contexts = new List<string>();
        if (checks.TryGetProperty("contexts", out var contextsValue) && contextsValue.ValueKind == JsonValueKind.Array)
        {
            contexts.AddRange(contextsValue.EnumerateArray().Select(item => item.GetString() ?? string.Empty));
        }

        if (checks.TryGetProperty("checks", out var checksValue) && checksValue.ValueKind == JsonValueKind.Array)
        {
            contexts.AddRange(checksValue.EnumerateArray()
                .Select(item => item.TryGetProperty("context", out var context) ? context.GetString() ?? string.Empty : string.Empty));
        }

        return contexts.Contains("agentos/quality-gate", StringComparer.Ordinal)
            ? ("required", null)
            : ("not_required", "agentos/quality-gate is not a required check.");
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("AgentOS/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(options.Value.ReviewToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ReviewToken);
        }
    }
}
