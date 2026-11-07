using AgentOS.Application.Approvals;
using AgentOS.Application.Decisions;
using AgentOS.Domain.Planning;
using AgentOS.Infrastructure.Approvals;
using AgentOS.Infrastructure.Decisions;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlDecisionMemoryTests
{
    [SkippableFact]
    public async Task Approved_decision_creates_memory_and_reference_tracks_hits()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var draft = new PlanningDraft
        {
            Id = Guid.CreateVersion7(),
            Title = "Memory test",
            Goal = "test",
            Status = PlanningDraftStatus.Approved,
            ContentMarkdown = "# test",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.PlanningDrafts.Add(draft);
        await context.SaveChangesAsync();

        var memoryStore = new DecisionMemoryStore(context);
        var approvals = new ApprovalService(
            new ApprovalRepository(context),
            TimeProvider.System,
            NullLogger<ApprovalService>.Instance,
            null,
            memoryStore);

        var created = await approvals.CreateAsync(new CreateApprovalRequest
        {
            TargetType = "PlannerDraft",
            TargetId = draft.Id.ToString(),
            RequestReason = "test"
        }, CancellationToken.None);
        await approvals.DecideAsync(created.Approval!.Id, new ApprovalDecisionRequest
        {
            Decision = "Approved",
            Reason = "looks good"
        }, CancellationToken.None);

        var memories = await memoryStore.ListAsync("Approved", "active", "PlannerDraft", draft.Id.ToString(), 10, 0, CancellationToken.None);
        var memory = Assert.Single(memories);
        Assert.Equal("Approved", memory.Decision);

        var references = await memoryStore.ReferencesAsync("PlannerDraft", draft.Id.ToString(), DateTime.UtcNow, CancellationToken.None);
        Assert.Single(references);
        Assert.Equal(1, references[0].HitCount);

        var invalidated = await memoryStore.InvalidateAsync(memory.Id, "no longer relevant", DateTime.UtcNow, CancellationToken.None);
        Assert.True(invalidated);
        Assert.Empty(await memoryStore.ReferencesAsync("PlannerDraft", draft.Id.ToString(), DateTime.UtcNow, CancellationToken.None));
    }
}
