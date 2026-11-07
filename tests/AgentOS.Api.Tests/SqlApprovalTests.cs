using AgentOS.Application.Approvals;
using AgentOS.Domain.CodingRuns;
using AgentOS.Infrastructure.Approvals;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlApprovalTests
{
    [SkippableFact]
    public async Task Creates_and_decides_an_approval()
    {
        var connectionString = RequireConnectionString();
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var draft = new AgentOS.Domain.Planning.PlanningDraft
        {
            Id = Guid.CreateVersion7(),
            Title = "Approval test",
            Goal = "Test approval",
            Status = AgentOS.Domain.Planning.PlanningDraftStatus.Approved,
            ContentMarkdown = "# test",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.PlanningDrafts.Add(draft);
        await context.SaveChangesAsync();
        var service = new ApprovalService(
            new ApprovalRepository(context),
            TimeProvider.System,
            NullLogger<ApprovalService>.Instance);

        var created = await service.CreateAsync(new CreateApprovalRequest
        {
            TargetType = "PlannerDraft",
            TargetId = draft.Id.ToString(),
            RequestReason = "please approve"
        }, CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Ok, created.Outcome);
        Assert.Equal("Pending", created.Approval!.Status);

        var duplicate = await service.CreateAsync(new CreateApprovalRequest
        {
            TargetType = "PlannerDraft",
            TargetId = draft.Id.ToString()
        }, CancellationToken.None);
        Assert.Equal(ApprovalOutcome.Conflict, duplicate.Outcome);

        var rejected = await service.DecideAsync(created.Approval.Id, new ApprovalDecisionRequest
        {
            Decision = "Rejected"
        }, CancellationToken.None);
        Assert.Equal(ApprovalOutcome.ValidationFailed, rejected.Outcome);

        var approved = await service.DecideAsync(created.Approval.Id, new ApprovalDecisionRequest
        {
            Decision = "Approved",
            Reason = "ok"
        }, CancellationToken.None);
        Assert.Equal("Approved", approved.Approval!.Status);
    }

    private static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        return connectionString!;
    }
}
