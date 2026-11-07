using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentOS.Api.GitHub;

public sealed record PullRequestMetadata(string HeadSha, string State);

public interface IGitHubPullRequestClient
{
    Task<PullRequestMetadata?> GetMetadataAsync(string repository, int number, CancellationToken cancellationToken);
    Task<string?> GetDiffAsync(string repository, int number, CancellationToken cancellationToken);
    Task<long?> CreateReviewAsync(string repository, int number, string headSha, string body, CancellationToken cancellationToken);
}

public sealed class GitHubPullRequestClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<GitHubOptions> options) : IGitHubPullRequestClient
{
    public async Task<PullRequestMetadata?> GetMetadataAsync(string repository, int number, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, repository, number, null);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

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
        return response.IsSuccessStatusCode
            ? await response.Content.ReadAsStringAsync(cancellationToken)
            : null;
    }

    public async Task<long?> CreateReviewAsync(
        string repository,
        int number,
        string headSha,
        string body,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, repository, number, "reviews");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { body, @event = "COMMENT", commit_id = headSha }),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("id", out var id) ? id.GetInt64() : null;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string repository, int number, string? suffix)
    {
        var token = options.Value.ReviewToken;
        var url = $"https://api.github.com/repos/{repository}/pulls/{number}" + (suffix is null ? "" : "/" + suffix);
        var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd("AgentOS/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }
}
