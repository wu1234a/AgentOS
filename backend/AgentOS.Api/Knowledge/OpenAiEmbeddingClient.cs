using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Knowledge;

public interface IEmbeddingClient
{
    bool Enabled { get; }
    Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken);
}

public sealed class OpenAiEmbeddingClient(
    HttpClient httpClient,
    IOptions<KnowledgeOptions> options) : IEmbeddingClient
{
    public bool Enabled => options.Value.Embedding.Enabled
        && !string.IsNullOrWhiteSpace(options.Value.Embedding.BaseUrl);

    public async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var configuration = options.Value.Embedding;
        if (!Enabled)
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{configuration.BaseUrl!.TrimEnd('/')}/embeddings");
        request.Headers.Accept.ParseAdd("application/json");
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        }

        request.Content = new StringContent(
            JsonSerializer.Serialize(new { model = configuration.Model, input = text }),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0)
        {
            return null;
        }

        var embedding = data[0].GetProperty("embedding");
        return embedding.EnumerateArray().Select(value => value.GetSingle()).ToArray();
    }
}
