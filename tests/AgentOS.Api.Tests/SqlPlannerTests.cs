using AgentOS.Application.Planning;
using AgentOS.Domain.ModelProfiles;
using AgentOS.Domain.Planning;
using AgentOS.Domain.Projects;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

/// <summary>AOS-012 acceptance tests for planner drafts and review decisions.</summary>
[Collection("SqlServer")]
public sealed class SqlPlannerTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 13, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Creates_pending_draft_and_supports_review_decisions()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var secretName = $"PLANNER_TEST_KEY_{Guid.NewGuid():N}".ToUpperInvariant();
        Environment.SetEnvironmentVariable(secretName, "test-api-key");

        var profile = await SeedProfileAsync(context, secretName, "api", "kimi");
        var project = await SeedProjectAsync(context);
        var client = new FakeCompletionClient
        {
            Result = new ApiCompletionResult(
                true,
                "# AOS-099: Draft\n## 1. 目标\nTest",
                null,
                null,
                200)
        };

        var service = CreateService(context, client, profile.Code);

        var created = await service.CreateAsync(
            new CreatePlanningDraftRequest
            {
                ProjectId = project.Id,
                Title = "Draft a task",
                Goal = "Add a planner draft feature with review gates.",
                Context = "AOS-012",
                ModelProfileId = profile.Id
            },
            CancellationToken.None);

        Assert.Equal(PlannerOutcome.Ok, created.Outcome);
        Assert.Equal("PendingReview", created.Draft!.Status);
        Assert.Contains("# AOS-099", created.Draft.ContentMarkdown);
        Assert.Equal(profile.Code, created.Draft.ModelProfileCode);
        Assert.NotNull(client.LastRequest);
        Assert.Equal("test-api-key", client.LastRequest!.ApiKey);
        Assert.Equal("kimi-model", client.LastRequest.Model);

        var approved = await service.ApproveAsync(created.Draft.Id, CancellationToken.None);
        Assert.Equal(PlannerOutcome.Ok, approved.Outcome);
        Assert.Equal("Approved", approved.Draft!.Status);
        Assert.NotNull(approved.Draft.DecidedAtUtc);

        var secondApproval = await service.ApproveAsync(created.Draft.Id, CancellationToken.None);
        Assert.Equal(PlannerOutcome.Conflict, secondApproval.Outcome);
        Assert.Equal("draft-already-decided", secondApproval.ErrorCode);

        var second = await service.CreateAsync(
            new CreatePlanningDraftRequest
            {
                Title = "Second draft",
                Goal = "Generate a second draft for rejection testing.",
                ModelProfileId = profile.Id
            },
            CancellationToken.None);
        var rejected = await service.RejectAsync(second.Draft!.Id, CancellationToken.None);
        Assert.Equal("Rejected", rejected.Draft!.Status);

        var list = await service.ListAsync("Approved", 10, 0, CancellationToken.None);
        Assert.Contains(list.Items, item => item.Id == created.Draft.Id);

        Environment.SetEnvironmentVariable(secretName, null);
    }

    [SkippableFact]
    public async Task Rejects_invalid_requests_and_unavailable_configuration()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var profile = await SeedProfileAsync(context, "MISSING_PLANNER_KEY_FOR_TEST", "api", "kimi");
        var service = CreateService(context, new FakeCompletionClient(), profile.Code);

        var shortGoal = await service.CreateAsync(
            new CreatePlanningDraftRequest { Title = "x", Goal = "short", ModelProfileId = profile.Id },
            CancellationToken.None);
        Assert.Equal(PlannerOutcome.ValidationFailed, shortGoal.Outcome);
        Assert.Contains("title", shortGoal.Errors.Keys);
        Assert.Contains("goal", shortGoal.Errors.Keys);

        var missingSecret = await service.CreateAsync(
            new CreatePlanningDraftRequest
            {
                Title = "Valid title",
                Goal = "A long enough goal for validation.",
                ModelProfileId = profile.Id
            },
            CancellationToken.None);
        Assert.Equal(PlannerOutcome.NotConfigured, missingSecret.Outcome);
        Assert.Equal("planner-secret-unavailable", missingSecret.ErrorCode);

        var cliProfile = await SeedProfileAsync(context, null, "cli", "codex-cli");
        var wrongMode = await service.CreateAsync(
            new CreatePlanningDraftRequest
            {
                Title = "Valid title",
                Goal = "A long enough goal for validation.",
                ModelProfileId = cliProfile.Id
            },
            CancellationToken.None);
        Assert.Equal(PlannerOutcome.ValidationFailed, wrongMode.Outcome);

        var providerFailure = await service.CreateAsync(
            new CreatePlanningDraftRequest
            {
                Title = "Provider failure",
                Goal = "A long enough goal for provider failure.",
                ModelProfileId = profile.Id
            },
            CancellationToken.None);
        Assert.Equal(PlannerOutcome.NotConfigured, providerFailure.Outcome);
    }

    private static PlannerService CreateService(
        AgentOSDbContext context,
        IApiCompletionClient client,
        string defaultCode) => new(
        new PlanningDraftRepository(context),
        new AgentOS.Infrastructure.ModelProfiles.ModelProfileRepository(context),
        client,
        Options.Create(new PlannerOptions { DefaultModelProfileCode = defaultCode }),
        TimeProvider.System,
        NullLogger<PlannerService>.Instance);

    private static async Task<ModelProfile> SeedProfileAsync(
        AgentOSDbContext context,
        string? secretReference,
        string mode,
        string provider)
    {
        var code = $"kimi-{Guid.NewGuid():N}"[..18];
        var profile = new ModelProfile
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Provider = provider,
            Model = "kimi-model",
            ExecutionMode = mode,
            ConfigurationJson = """{"baseUrl":"https://api.moonshot.cn/v1","temperature":0.2}""",
            SecretReference = secretReference,
            TimeoutSeconds = 60,
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.ModelProfiles.Add(profile);
        await context.SaveChangesAsync();
        return profile;
    }

    private static async Task<Project> SeedProjectAsync(AgentOSDbContext context)
    {
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Planner",
            Slug = $"planner-{Guid.NewGuid():N}"[..20],
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project;
    }

    private static async Task MigrateAsync(string connectionString)
    {
        await using var context = NewContext(connectionString);
        await context.Database.MigrateAsync();
    }

    private static AgentOSDbContext NewContext(string connectionString)
        => new(new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);

    private static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION to a disposable database.");
        return connectionString!;
    }

    private sealed class FakeCompletionClient : IApiCompletionClient
    {
        public ApiCompletionRequest? LastRequest { get; private set; }

        public ApiCompletionResult Result { get; set; } = new(
            true,
            "# AOS-099: Draft",
            null,
            null,
            200);

        public Task<ApiCompletionResult> CompleteAsync(
            ApiCompletionRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }
}
