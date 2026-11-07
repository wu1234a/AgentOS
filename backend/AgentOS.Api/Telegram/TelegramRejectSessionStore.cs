using AgentOS.Domain.Telegram;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.Telegram;

public interface ITelegramRejectSessionStore
{
    Task<TelegramRejectSession?> GetActiveAsync(long chatId, long userId, CancellationToken cancellationToken);
    Task CreateAsync(Guid approvalId, long chatId, long userId, long messageId, CancellationToken cancellationToken);
    Task CompleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class TelegramRejectSessionStore(AgentOSDbContext dbContext, TimeProvider timeProvider) : ITelegramRejectSessionStore
{
    public Task<TelegramRejectSession?> GetActiveAsync(
        long chatId,
        long userId,
        CancellationToken cancellationToken)
        => dbContext.TelegramRejectSessions.FirstOrDefaultAsync(
            session => session.ChatId == chatId
                && session.UserId == userId
                && session.Status == "AwaitingReason"
                && session.ExpiresAtUtc > timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

    public async Task CreateAsync(
        Guid approvalId,
        long chatId,
        long userId,
        long messageId,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.TelegramRejectSessions
            .Where(session => session.ApprovalId == approvalId
                || (session.ChatId == chatId && session.UserId == userId))
            .ToListAsync(cancellationToken);
        dbContext.TelegramRejectSessions.RemoveRange(existing);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        dbContext.TelegramRejectSessions.Add(new TelegramRejectSession
        {
            Id = Guid.CreateVersion7(),
            ApprovalId = approvalId,
            ChatId = chatId,
            UserId = userId,
            MessageId = messageId,
            Status = "AwaitingReason",
            ExpiresAtUtc = now.AddMinutes(5),
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var session = await dbContext.TelegramRejectSessions.FirstOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (session is not null)
        {
            dbContext.TelegramRejectSessions.Remove(session);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
