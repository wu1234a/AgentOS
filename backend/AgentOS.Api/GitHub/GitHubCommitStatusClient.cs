using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentOS.Api.GitHub;

public interface IGitHubCommitStatusClient
{
    Task<bool> PostAsync(
        string repository,
        string sha,
        string state,
        string description,
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
}
