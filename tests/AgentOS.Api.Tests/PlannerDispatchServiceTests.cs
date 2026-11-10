using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class PlannerDispatchServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Disabled_dispatch_does_nothing()
    {
        var service = CreateService(new PlannerAutoDispatchOptions { Enabled = false });

        var result = await service.DispatchAsync(Draft(Guid.CreateVersion7()), CancellationToken.None);

        Assert.Equal(PlannerDispatchOutcome.Disabled, result.Outcome);
    }

    [Fact]
    public async Task Skips_drafts_for_other_projects()
    {
        var projects = new FakeProjectLookup();
        var service = CreateService(
            new PlannerAutoDispatchOptions
            {
                Enabled = true,
                ProjectSlug = "another-project",
                Delivery = new PlannerAutoDeliveryOptions { Repository = "owner/repo" }
            },
            projects);

        var result = await service.DispatchAsync(Draft(projects.Enabled.Id), CancellationToken.None);

        Assert.Equal(PlannerDispatchOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task Fails_when_delivery_repository_is_missing()
    {
        var projects = new FakeProjectLookup();
        var service = CreateService(
            new PlannerAutoDispatchOptions
            {
                Enabled = true,
                ProjectSlug = projects.Enabled.Slug
            },
            projects);

        var result = await service.DispatchAsync(Draft(projects.Enabled.Id), CancellationToken.None);

        Assert.Equal(PlannerDispatchOutcome.Failed, result.Outcome);
        Assert.Equal("planner-dispatch-delivery-unconfigured", result.ErrorCode);
    }

    private static PlannerDispatchService CreateService(
        PlannerAutoDispatchOptions autoDispatch,
        FakeProjectLookup? projects = null)
        => new(
            null!,
            projects ?? new FakeProjectLookup(),
            Options.Create(new PlannerOptions { AutoDispatch = autoDispatch }),
            TimeProvider.System,
            NullLogger<PlannerDispatchService>.Instance);

    private static PlanningDraftView Draft(Guid projectId)
        => new(
            Guid.CreateVersion7(),
            projectId,
            "A test draft",
            "Implement a test draft.",
            null,
            null,
            "kimi-planner",
            "Approved",
            "# Plan",
            null,
            Now,
            Now,
            Now);
}
