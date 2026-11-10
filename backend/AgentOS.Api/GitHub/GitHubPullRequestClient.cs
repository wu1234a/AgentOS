using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentOS.Api.GitHub;

public sealed record PullRequestMetadata(string HeadSha, string State);

public sealed record PullRequestMergeInfo(
    string State,
    bool Draft,
    bool? Mergeable,
    string? MergeableState,
    string HeadSha,
    string BaseRef);

public sealed record PullRequestSummary(
    int Number,
    string State,
    bool Draft,
    bool? Mergeable,
    string HeadSha,
    string HeadRef,
    string BaseRef);

public sealed record PullRequestMergeResult(bool Success, string? Error);

public interface IGitHubPullRequestClient
{
    Task<PullRequestMetadata?> GetMetadataAsync(string repository, int number, CancellationToken cancellationToken);
    Task<string?> GetDiffAsync(string repository, int number, CancellationToken cancellationToken);
    Task<long?> CreateReviewAsync(string repository, int number, string headSha, string body, CancellationToken cancellationToken);
    Task<PullRequestMergeInfo?> GetMergeInfoAsync(string repository, int number, CancellationToken cancellationToken);
    Task<int?> GetApprovalCountAsync(string repository, int number, CancellationToken cancellationToken);
    Task<string?> GetCombinedStatusAsync(string repository, string sha, CancellationToken cancellationToken);
    Task<IReadOnlyList<PullRequestSummary>?> ListOpenPullRequestsAsync(string repository, CancellationToken cancellationToken);
    Task<PullRequestMergeResult> MergeAsync(string repository, int number, string headSha, string mergeMethod, CancellationToken cancellationToken);
}

public sealed class GitHubPullRequestClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<GitHubOptions> options) : IGitHubPullRequestClient
{
    public async Task<PullRequestMetadata?> GetMetadataAsync(string repository, int number, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, repository, number, null);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        return new PullRequestMetadata(
            root.GetProperty("head").GetProperty("sha").GetString() ?? string.Empty,
            root.GetProperty("state").GetString() ?? "open");
    }

    public async Task<string?> GetDiffAsync(string repository, int number, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, repository, number, null);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.diff"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
    }

    public async Task<long?> CreateReviewAsync(string repository, int number, string headSha, string body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, repository, number, "reviews");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { body, @event = "COMMENT", commit_id = headSha }),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("id", out var id) ? id.GetInt64() : null;
    }

    public async Task<PullRequestMergeInfo?> GetMergeInfoAsync(string repository, int number, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, repository, number, null);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        return new PullRequestMergeInfo(
            root.TryGetProperty("state", out var state) ? state.GetString() ?? "unknown" : "unknown",
            root.TryGetProperty("draft", out var draft) && draft.GetBoolean(),
            root.TryGetProperty("mergeable", out var mergeable) && mergeable.ValueKind is JsonValueKind.True or JsonValueKind.False ? mergeable.GetBoolean() : null,
            root.TryGetProperty("mergeable_state", out var mergeState) ? mergeState.GetString() : null,
            root.TryGetProperty("head", out var head) && head.TryGetProperty("sha", out var sha) ? sha.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("base", out var baseBranch) && baseBranch.TryGetProperty("ref", out var reference) ? reference.GetString() ?? string.Empty : string.Empty);
    }

    public async Task<int?> GetApprovalCountAsync(string repository, int number, CancellationToken cancellationToken)
    {
        var latest = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var page = 1; page <= 10; page++)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repository}/pulls/{number}/reviews?per_page=100&page={page}");
            AddHeaders(request);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var reviews = document.RootElement.EnumerateArray().ToList();
            foreach (var review in reviews)
            {
                var user = review.TryGetProperty("user", out var userElement) && userElement.TryGetProperty("login", out var login) ? login.GetString() : null;
                var state = review.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;
                if (user is not null && state is not null) latest[user] = state;
            }
            if (reviews.Count < 100) break;
        }
        return latest.Values.Count(state => state == "APPROVED");
    }

    public async Task<IReadOnlyList<PullRequestSummary>?> ListOpenPullRequestsAsync(
        string repository,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository}/pulls?state=open&per_page=100&sort=created&direction=asc");
        AddHeaders(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<PullRequestSummary>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("number", out var number)
                || !item.TryGetProperty("head", out var head)
                || !head.TryGetProperty("sha", out var sha)
                || !head.TryGetProperty("ref", out var headRef)
                || !item.TryGetProperty("base", out var baseElement)
                || !baseElement.TryGetProperty("ref", out var baseRef))
            {
                continue;
            }

            items.Add(new PullRequestSummary(
                number.GetInt32(),
                item.TryGetProperty("state", out var state) ? state.GetString() ?? "unknown" : "unknown",
                item.TryGetProperty("draft", out var draft) && draft.GetBoolean(),
                item.TryGetProperty("mergeable", out var mergeable)
                    && mergeable.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? mergeable.GetBoolean()
                        : null,
                sha.GetString() ?? string.Empty,
                headRef.GetString() ?? string.Empty,
                baseRef.GetString() ?? string.Empty));
        }

        return items;
    }

    public async Task<PullRequestMergeResult> MergeAsync(
        string repository,
        int number,
        string headSha,
        string mergeMethod,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, repository, number, "merge");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { sha = headSha, merge_method = mergeMethod }),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new PullRequestMergeResult(false, $"GitHub returned HTTP {(int)response.StatusCode}: {body}");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("merged", out var merged) && merged.GetBoolean())
            {
                return new PullRequestMergeResult(true, null);
            }

            var message = document.RootElement.TryGetProperty("message", out var error)
                ? error.GetString()
                : null;
            return new PullRequestMergeResult(false, message ?? "GitHub did not merge the pull request.");
        }
        catch (JsonException)
        {
            return new PullRequestMergeResult(false, "GitHub returned an invalid merge response.");
        }
    }

    public async Task<string?> GetCombinedStatusAsync(string repository, string sha, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/commits/{sha}/status");
        AddHeaders(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("state", out var state) ? state.GetString() : null;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string repository, int number, string? suffix)
    {
        var url = $"https://api.github.com/repos/{repository}/pulls/{number}" + (suffix is null ? string.Empty : "/" + suffix);
        var request = new HttpRequestMessage(method, url);
        AddHeaders(request);
        return request;
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
