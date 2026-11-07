using AgentOS.Api.Telegram;
using AgentOS.Application.Approvals;
using AgentOS.Domain.Planning;
using AgentOS.Infrastructure.Approvals;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlTelegramOutboxTests
{
    [SkippableFact]
    public async Task Approval_creation_enqueues_one_telegram_notification()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var draft = new PlanningDraft
        {
            Id = Guid.CreateVersion7(),
            Title = "Telegram outbox",
            Goal = "test",
            Status = PlanningDraftStatus.Approved,
            ContentMarkdown = "# test",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.PlanningDrafts.Add(draft);
        await context.SaveChangesAsync();

        var options = Options.Create(new TelegramOptions
        {
            Enabled = true,
            NotificationChatId = 12345
        });
        var outbox = new TelegramOutbox(context, options, TimeProvider.System);
        var service = new ApprovalService(
            new ApprovalRepository(context),
            TimeProvider.System,
            NullLogger<ApprovalService>.Instance,
            outbox);

        var created = await service.CreateAsync(new CreateApprovalRequest
        {
            TargetType = "PlannerDraft",
            TargetId = draft.Id.ToString()
        }, CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Ok, created.Outcome);
        Assert.Single(await context.TelegramNotifications
            .Where(notification => notification.ApprovalId == created.Approval!.Id)
            .ToListAsync());
    }
}
