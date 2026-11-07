using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Telegram;

public interface ITelegramClient
{
    Task<IReadOnlyList<JsonElement>> GetUpdatesAsync(long offset, CancellationToken cancellationToken);
    Task<long> SendMessageAsync(long chatId, string text, string? callbackData, CancellationToken cancellationToken);
    Task AnswerCallbackAsync(string callbackId, string text, CancellationToken cancellationToken);
    Task EditMessageAsync(long chatId, long messageId, string text, CancellationToken cancellationToken);
}

public sealed class HttpTelegramClient(
    HttpClient httpClient,
    IOptions<TelegramOptions> options) : ITelegramClient
{
    private string Token => options.Value.BotToken ?? string.Empty;

    public async Task<IReadOnlyList<JsonElement>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(
            $"https://api.telegram.org/bot{Token}/getUpdates?timeout={options.Value.PollTimeoutSeconds}&offset={offset}",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return result.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    public async Task<long> SendMessageAsync(long chatId, string text, string? callbackData, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = chatId,
            ["text"] = text
        };

        if (!string.IsNullOrWhiteSpace(callbackData))
        {
            payload["reply_markup"] = new
            {
                inline_keyboard = new[]
                {
                    new[]
                    {
                        new { text = "批准", callback_data = $"approve:{callbackData}" },
                        new { text = "拒绝", callback_data = $"reject:{callbackData}" }
                    }
                }
            };
        }

        using var response = await PostForResponseAsync("sendMessage", payload, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("result", out var result)
            && result.TryGetProperty("message_id", out var messageId)
            ? messageId.GetInt64()
            : 0;
    }

    public Task AnswerCallbackAsync(string callbackId, string text, CancellationToken cancellationToken)
        => PostAsync("answerCallbackQuery", new { callback_query_id = callbackId, text }, cancellationToken);

    public Task EditMessageAsync(long chatId, long messageId, string text, CancellationToken cancellationToken)
        => PostAsync("editMessageText", new { chat_id = chatId, message_id = messageId, text }, cancellationToken);

    private async Task PostAsync(string method, object payload, CancellationToken cancellationToken)
    {
        using var response = await PostForResponseAsync(method, payload, cancellationToken);
    }

    private async Task<HttpResponseMessage> PostForResponseAsync(
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync($"https://api.telegram.org/bot{Token}/{method}", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response;
    }
}
