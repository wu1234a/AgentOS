using System.Text.Json;
using AgentOS.Api.Telegram;
using AgentOS.Application.Approvals;
using AgentOS.Domain.Approvals;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class TelegramUpdateProcessorTests
{
    [Fact]
    public async Task Pending_command_sends_cards_to_an_allowed_chat()
    {
        var repository = new FakeApprovalRepository();
        repository.Requests.Add(NewApproval());
        var client = new FakeTelegramClient();
        var processor = CreateProcessor(repository, client);

        await processor.HandleAsync(Message(100, 200, "/pending"), CancellationToken.None);

        Assert.Single(client.Messages);
        Assert.Equal(100, client.Messages[0].ChatId);
    }

    [Fact]
    public async Task Unauthorized_chat_gets_no_data()
    {
        var repository = new FakeApprovalRepository();
        repository.Requests.Add(NewApproval());
        var client = new FakeTelegramClient();
        var processor = CreateProcessor(repository, client);

        await processor.HandleAsync(Message(999, 200, "/pending"), CancellationToken.None);

        Assert.Empty(client.Messages);
    }

    [Fact]
    public async Task Approve_callback_decides_through_the_approval_service()
    {
        var repository = new FakeApprovalRepository();
        var approval = NewApproval();
        repository.Requests.Add(approval);
        var client = new FakeTelegramClient();
        var processor = CreateProcessor(repository, client);

        await processor.HandleAsync(Callback(100, 200, $"approve:{approval.Id}"), CancellationToken.None);

        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Contains(client.Answers, answer => answer.Contains("已批准"));
        Assert.Single(client.Edits);
    }

    private static TelegramUpdateProcessor CreateProcessor(
        FakeApprovalRepository repository,
        FakeTelegramClient client)
        => new(
            new ApprovalService(repository, TimeProvider.System, NullLogger<ApprovalService>.Instance),
            client,
            new FakeRejectSessions(),
            Options.Create(new TelegramOptions
            {
                AllowedChatIds = [100],
                AllowedUserIds = [200]
            }));

    private static ApprovalRequest NewApproval() => new()
    {
        Id = Guid.NewGuid(),
        TargetType = ApprovalTargetType.CodingRun,
        TargetId = Guid.NewGuid().ToString("D"),
        Status = ApprovalStatus.Pending,
        RequestReason = "test",
        RequestedByUserId = "dev-user",
        RequestedAtUtc = DateTime.UtcNow
    };

    private static JsonElement Message(long chatId, long userId, string text)
        => JsonDocument.Parse("{\"update_id\":1,\"message\":{\"message_id\":10,\"chat\":{\"id\":" + chatId + "},\"from\":{\"id\":" + userId + "},\"text\":\"" + text + "\"}}").RootElement.Clone();

    private static JsonElement Callback(long chatId, long userId, string data)
        => JsonDocument.Parse("{\"update_id\":2,\"callback_query\":{\"id\":\"cb\",\"from\":{\"id\":" + userId + "},\"data\":\"" + data + "\",\"message\":{\"message_id\":10,\"chat\":{\"id\":" + chatId + "}}}}").RootElement.Clone();
    private sealed class FakeRejectSessions : ITelegramRejectSessionStore
    {
        public Task<AgentOS.Domain.Telegram.TelegramRejectSession?> GetActiveAsync(long chatId, long userId, CancellationToken cancellationToken)
            => Task.FromResult<AgentOS.Domain.Telegram.TelegramRejectSession?>(null);

        public Task CreateAsync(Guid approvalId, long chatId, long userId, long messageId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task CompleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeTelegramClient : ITelegramClient
    {
        public List<(long ChatId, string Text)> Messages { get; } = [];
        public List<string> Answers { get; } = [];
        public List<(long ChatId, long MessageId, string Text)> Edits { get; } = [];

        public Task<IReadOnlyList<JsonElement>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<JsonElement>>([]);

        public Task<long> SendMessageAsync(long chatId, string text, string? callbackData, CancellationToken cancellationToken)
        {
            Messages.Add((chatId, text));
            return Task.FromResult(1L);
        }

        public Task AnswerCallbackAsync(string callbackId, string text, CancellationToken cancellationToken)
        {
            Answers.Add(text);
            return Task.CompletedTask;
        }

        public Task EditMessageAsync(long chatId, long messageId, string text, CancellationToken cancellationToken)
        {
            Edits.Add((chatId, messageId, text));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeApprovalRepository : IApprovalRepository
    {
        public List<ApprovalRequest> Requests { get; } = [];

        public Task<bool> TargetExistsAsync(ApprovalTargetType type, string targetId, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<ApprovalRequest?> GetTrackedAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(Requests.FirstOrDefault(request => request.Id == id));

        public Task<ApprovalRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(Requests.FirstOrDefault(request => request.Id == id));

        public Task<ApprovalRequest?> GetPendingAsync(ApprovalTargetType type, string targetId, CancellationToken cancellationToken)
            => Task.FromResult(Requests.FirstOrDefault(request => request.TargetType == type && request.TargetId == targetId && request.Status == ApprovalStatus.Pending));

        public Task<IReadOnlyList<ApprovalRequest>> ListAsync(string? status, string? targetType, int limit, int offset, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ApprovalRequest>>(Requests
                .Where(request => request.Status == ApprovalStatus.Pending)
                .Skip(offset)
                .Take(limit)
                .ToList());

        public Task<int> CountAsync(string? status, string? targetType, CancellationToken cancellationToken)
            => Task.FromResult(Requests.Count);

        public Task<bool> TryAddAsync(ApprovalRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(true);
        }

        public Task<bool> TryDecideAsync(Guid id, ApprovalStatus status, string decidedBy, string? reason, DateTime decidedAtUtc, CancellationToken cancellationToken)
        {
            var request = Requests.FirstOrDefault(candidate => candidate.Id == id && candidate.Status == ApprovalStatus.Pending);
            if (request is null)
            {
                return Task.FromResult(false);
            }

            request.Status = status;
            request.DecidedByUserId = decidedBy;
            request.DecidedAtUtc = decidedAtUtc;
            request.DecisionReason = reason;
            return Task.FromResult(true);
        }

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
