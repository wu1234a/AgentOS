using System.Text.Json;
using AgentOS.Application.Approvals;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Telegram;

public sealed class TelegramUpdateProcessor(
    ApprovalService approvals,
    ITelegramClient client,
    IOptions<TelegramOptions> options)
{
    private const int MaxPending = 10;

    public async Task HandleAsync(JsonElement update, CancellationToken cancellationToken)
    {
        if (update.TryGetProperty("callback_query", out var callback))
        {
            await HandleCallbackAsync(callback, cancellationToken);
            return;
        }

        if (update.TryGetProperty("message", out var message))
        {
            await HandleMessageAsync(message, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var chatId = message.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var chatIdValue)
            ? chatIdValue.GetInt64()
            : 0;
        var userId = message.TryGetProperty("from", out var from) && from.TryGetProperty("id", out var userIdValue)
            ? userIdValue.GetInt64()
            : 0;
        var text = message.TryGetProperty("text", out var textValue) ? textValue.GetString() ?? string.Empty : string.Empty;

        if (!IsAllowed(chatId, userId) || !text.StartsWith("/pending", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var (items, _) = await approvals.ListAsync("Pending", null, MaxPending, 0, cancellationToken);
        if (items.Count == 0)
        {
            await client.SendMessageAsync(chatId, "没有待审批请求。", null, cancellationToken);
            return;
        }

        foreach (var item in items)
        {
            var textMessage = $"待审批：{item.TargetType} {item.TargetId}\n申请理由：{item.RequestReason ?? "-"}";
            await client.SendMessageAsync(chatId, textMessage, item.Id.ToString("D"), cancellationToken);
        }
    }

    private async Task HandleCallbackAsync(JsonElement callback, CancellationToken cancellationToken)
    {
        var callbackId = callback.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
        var data = callback.TryGetProperty("data", out var dataValue) ? dataValue.GetString() ?? string.Empty : string.Empty;
        var userId = callback.TryGetProperty("from", out var from) && from.TryGetProperty("id", out var userIdValue)
            ? userIdValue.GetInt64()
            : 0;

        if (!callback.TryGetProperty("message", out var message)
            || !message.TryGetProperty("chat", out var chat)
            || !chat.TryGetProperty("id", out var chatIdValue))
        {
            await client.AnswerCallbackAsync(callbackId, "无法识别审批消息。", cancellationToken);
            return;
        }

        var chatId = chatIdValue.GetInt64();
        var messageId = message.TryGetProperty("message_id", out var messageIdValue) ? messageIdValue.GetInt64() : 0;

        if (!IsAllowed(chatId, userId))
        {
            await client.AnswerCallbackAsync(callbackId, "无权限。", cancellationToken);
            return;
        }

        var parts = data.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out var approvalId))
        {
            await client.AnswerCallbackAsync(callbackId, "无效的审批操作。", cancellationToken);
            return;
        }

        var decision = parts[0] switch
        {
            "approve" => "Approved",
            "reject" => "Rejected",
            _ => string.Empty
        };

        if (decision.Length == 0)
        {
            await client.AnswerCallbackAsync(callbackId, "无效的审批操作。", cancellationToken);
            return;
        }

        var result = await approvals.DecideAsync(
            approvalId,
            new ApprovalDecisionRequest { Decision = decision, Reason = decision == "Rejected" ? "Rejected via Telegram" : "Approved via Telegram" },
            cancellationToken);

        var answer = result.Outcome switch
        {
            ApprovalOutcome.Ok => decision == "Approved" ? "已批准。" : "已拒绝。",
            ApprovalOutcome.Conflict => "该审批已处理。",
            ApprovalOutcome.NotFound => "审批不存在。",
            _ => "审批失败。"
        };

        await client.AnswerCallbackAsync(callbackId, answer, cancellationToken);

        if (result.Outcome == ApprovalOutcome.Ok && messageId > 0)
        {
            await client.EditMessageAsync(chatId, messageId, answer, cancellationToken);
        }
    }

    private bool IsAllowed(long chatId, long userId)
    {
        var configuration = options.Value;
        return configuration.AllowedChatIds.Contains(chatId)
            && configuration.AllowedUserIds.Contains(userId);
    }
}
