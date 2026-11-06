using AgentOS.Application.Skills;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Skills;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Rules;
using AgentOS.Infrastructure.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

/// <summary>AOS-011 acceptance tests for skill versions and content storage.</summary>
[Collection("SqlServer")]
public sealed class SqlSkillTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Creates_versions_with_content_hash_and_rejects_duplicates()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var root = Path.Combine(Path.GetTempPath(), "agentos-skill-tests", Guid.NewGuid().ToString("N"));
        var service = CreateService(context, root);
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var code = $"bugfix-sop-{suffix}";

        var created = await service.CreateAsync(
            new CreateSkillRequest
            {
                Code = code,
                Version = "1.0.0",
                Description = "Bugfix SOP",
                Content = "# Bugfix\n1. Reproduce"
            },
            CancellationToken.None);

        Assert.Equal(SkillOutcome.Ok, created.Outcome);
        Assert.Equal(64, created.Skill!.ContentHash!.Length);
        Assert.True(File.Exists(Path.Combine(root, created.Skill.RelativePath.Replace('/', Path.DirectorySeparatorChar))));

        var duplicate = await service.CreateAsync(
            new CreateSkillRequest
            {
                Code = code,
                Version = "1.0.0",
                Content = "other"
            },
            CancellationToken.None);
        Assert.Equal(SkillOutcome.Conflict, duplicate.Outcome);
        Assert.Equal("skill-version-conflict", duplicate.ErrorCode);

        var second = await service.CreateAsync(
            new CreateSkillRequest
            {
                Code = code,
                Version = "2.0.0",
                Content = "# Bugfix v2"
            },
            CancellationToken.None);
        Assert.Equal(SkillOutcome.Ok, second.Outcome);

        var content = await service.GetContentAsync(created.Skill.Id, CancellationToken.None);
        Assert.Equal("# Bugfix\n1. Reproduce", content);

        var updated = await service.UpdateAsync(
            created.Skill.Id,
            new UpdateSkillRequest { Description = "Updated SOP" },
            CancellationToken.None);
        Assert.Equal("Updated SOP", updated.Skill!.Description);

        var disabled = await service.DisableAsync(created.Skill.Id, CancellationToken.None);
        Assert.False(disabled.Skill!.IsEnabled);

        var list = await service.ListAsync(null, code, true, 10, 0, CancellationToken.None);
        Assert.Equal(2, list.Total);
    }

    [SkippableFact]
    public async Task Global_versions_are_unique_and_project_scope_is_separate()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var project = await SeedProjectAsync(context);
        var service = CreateService(context, Path.Combine(Path.GetTempPath(), "agentos-skill-tests", Guid.NewGuid().ToString("N")));
        var code = $"review-sop-{Guid.NewGuid().ToString("N")[..6]}";

        var global = await service.CreateAsync(
            new CreateSkillRequest { Code = code, Version = "1.0.0", Content = "global" },
            CancellationToken.None);
        Assert.Equal(SkillOutcome.Ok, global.Outcome);

        var globalDuplicate = await service.CreateAsync(
            new CreateSkillRequest { Code = code, Version = "1.0.0", Content = "again" },
            CancellationToken.None);
        Assert.Equal(SkillOutcome.Conflict, globalDuplicate.Outcome);

        var projectVersion = await service.CreateAsync(
            new CreateSkillRequest
            {
                ProjectId = project.Id,
                Code = code,
                Version = "1.0.0",
                Content = "project"
            },
            CancellationToken.None);
        Assert.Equal(SkillOutcome.Ok, projectVersion.Outcome);

        var lookup = new RuleReferenceLookup(context);
        var latest = await lookup.FindSkillAsync(code, project.Id, CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal("project", latest!.ProjectId == project.Id ? "project" : "global");
    }

    [SkippableFact]
    public async Task Latest_version_wins_and_validation_rejects_bad_input()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var root = Path.Combine(Path.GetTempPath(), "agentos-skill-tests", Guid.NewGuid().ToString("N"));
        var service = CreateService(context, root);
        var code = $"coding-sop-{Guid.NewGuid().ToString("N")[..6]}";

        await service.CreateAsync(
            new CreateSkillRequest { Code = code, Version = "1.0.0", Content = "v1" },
            CancellationToken.None);
        await Task.Delay(5);
        await service.CreateAsync(
            new CreateSkillRequest { Code = code, Version = "1.1.0", Content = "v2" },
            CancellationToken.None);

        var lookup = new RuleReferenceLookup(context);
        var latest = await lookup.FindSkillAsync(code, null, CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal("1.1.0", latest!.Version);

        var badCode = await service.CreateAsync(
            new CreateSkillRequest { Code = "Bad Code!", Version = "1.0.0", Content = "x" },
            CancellationToken.None);
        Assert.Equal(SkillOutcome.ValidationFailed, badCode.Outcome);

        var badVersion = await service.CreateAsync(
            new CreateSkillRequest { Code = "ok-code", Version = "../escape", Content = "x" },
            CancellationToken.None);
        Assert.Equal(SkillOutcome.ValidationFailed, badVersion.Outcome);

        var emptyContent = await service.CreateAsync(
            new CreateSkillRequest { Code = "ok-code", Version = "1.0.0", Content = "  " },
            CancellationToken.None);
        Assert.Equal(SkillOutcome.ValidationFailed, emptyContent.Outcome);

        var unavailable = CreateService(context, root, configured: false);
        var noStorage = await unavailable.CreateAsync(
            new CreateSkillRequest { Code = "ok-code", Version = "1.0.0", Content = "x" },
            CancellationToken.None);
        Assert.Equal(SkillOutcome.StorageUnavailable, noStorage.Outcome);
    }

    private static SkillService CreateService(
        AgentOSDbContext context,
        string root,
        bool configured = true)
    {
        var options = Options.Create(new SkillContentOptions
        {
            RootPath = configured ? root : string.Empty,
            MaxBytes = 65536
        });

        return new SkillService(
            new SkillRepository(context),
            new SkillStore(options, NullLogger<SkillStore>.Instance),
            TimeProvider.System);
    }

    private static async Task<Project> SeedProjectAsync(AgentOSDbContext context)
    {
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Skill Registry",
            Slug = $"skills-{Guid.NewGuid():N}"[..20],
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
}
