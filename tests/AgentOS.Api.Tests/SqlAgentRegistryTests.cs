using AgentOS.Application.Agents;
using AgentOS.Domain.Agents;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Agents;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

/// <summary>AOS-009 acceptance tests for the agent registry and lifecycle.</summary>
[Collection("SqlServer")]
public sealed class SqlAgentRegistryTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Create_list_update_and_duplicate_code()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var project = await SeedProjectAsync(context);
        var service = CreateService(context);

        var created = await service.CreateAsync(
            new CreateAgentRequest
            {
                ProjectId = project.Id,
                Code = "agent-coder",
                DisplayName = "Coder",
                Role = "coder",
                PermissionProfile = "read-only",
                Description = "Primary coding agent"
            },
            CancellationToken.None);

        Assert.Equal(AgentOutcome.Ok, created.Outcome);
        Assert.True(created.Agent!.IsEnabled);
        Assert.Null(created.Agent.ArchivedAtUtc);
        Assert.Equal(project.Slug, created.Agent.ProjectSlug);

        var duplicate = await service.CreateAsync(
            new CreateAgentRequest
            {
                ProjectId = project.Id,
                Code = "agent-coder",
                DisplayName = "Duplicate",
                Role = "coder",
                PermissionProfile = "read-only"
            },
            CancellationToken.None);

        Assert.Equal(AgentOutcome.Conflict, duplicate.Outcome);
        Assert.Equal("agent-code-conflict", duplicate.ErrorCode);

        var updated = await service.UpdateAsync(
            created.Agent.Id,
            new UpdateAgentRequest
            {
                DisplayName = "Coder v2",
                Role = "reviewer",
                PermissionProfile = "workspace-write"
            },
            CancellationToken.None);

        Assert.Equal(AgentOutcome.Ok, updated.Outcome);
        Assert.Equal("Coder v2", updated.Agent!.DisplayName);
        Assert.Equal("reviewer", updated.Agent.Role);

        var list = await service.ListAsync(project.Id, "reviewer", false, 10, 0, CancellationToken.None);
        Assert.Equal(1, list.Total);
        Assert.Equal(created.Agent.Id, list.Items[0].Id);
    }

    [SkippableFact]
    public async Task Validation_rejects_bad_role_permission_and_prompt_paths()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var service = CreateService(context);

        var badRole = await service.CreateAsync(
            new CreateAgentRequest
            {
                Code = "agent-bad-role",
                DisplayName = "Bad",
                Role = "hacker",
                PermissionProfile = "read-only"
            },
            CancellationToken.None);
        Assert.Equal(AgentOutcome.ValidationFailed, badRole.Outcome);
        Assert.Contains("role", badRole.Errors.Keys);

        var badPermission = await service.CreateAsync(
            new CreateAgentRequest
            {
                Code = "agent-bad-perm",
                DisplayName = "Bad",
                Role = "coder",
                PermissionProfile = "root"
            },
            CancellationToken.None);
        Assert.Equal(AgentOutcome.ValidationFailed, badPermission.Outcome);

        var traversal = await service.CreateAsync(
            new CreateAgentRequest
            {
                Code = "agent-bad-path",
                DisplayName = "Bad",
                Role = "coder",
                PermissionProfile = "read-only",
                SystemPromptPath = "../secrets.txt"
            },
            CancellationToken.None);
        Assert.Equal(AgentOutcome.ValidationFailed, traversal.Outcome);
        Assert.Contains("systemPromptPath", traversal.Errors.Keys);

        var absolute = await service.CreateAsync(
            new CreateAgentRequest
            {
                Code = "agent-abs-path",
                DisplayName = "Bad",
                Role = "coder",
                PermissionProfile = "read-only",
                SystemPromptPath = "C:\\secrets.txt"
            },
            CancellationToken.None);
        Assert.Equal(AgentOutcome.ValidationFailed, absolute.Outcome);
    }

    [SkippableFact]
    public async Task Disable_blocks_new_rule_references_and_archive_requires_no_active_tasks()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var project = await SeedProjectAsync(context);
        var service = CreateService(context);

        var created = await service.CreateAsync(
            new CreateAgentRequest
            {
                ProjectId = project.Id,
                Code = "agent-lifecycle",
                DisplayName = "Lifecycle",
                Role = "coder",
                PermissionProfile = "read-only"
            },
            CancellationToken.None);
        var agentId = created.Agent!.Id;

        var disabled = await service.DisableAsync(agentId, CancellationToken.None);
        Assert.Equal(AgentOutcome.Ok, disabled.Outcome);
        Assert.False(disabled.Agent!.IsEnabled);

        var lookup = new Infrastructure.Rules.RuleReferenceLookup(context);
        var reference = await lookup.FindAgentAsync("agent-lifecycle", project.Id, CancellationToken.None);
        Assert.NotNull(reference);
        Assert.False(reference!.IsEnabled);

        var enabled = await service.EnableAsync(agentId, CancellationToken.None);
        Assert.True(enabled.Agent!.IsEnabled);

        var task = new AutomationTask
        {
            Id = Guid.CreateVersion7(),
            TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
            ProjectId = project.Id,
            AgentId = agentId,
            LogicalTaskKey = $"agent-{Guid.NewGuid():N}",
            Status = TaskStatus.Queued,
            Priority = "normal",
            Title = "Active task",
            InputJson = "{}",
            AttemptCount = 0,
            MaxAttempts = 2,
            NextRunAtUtc = Now,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        var blocked = await service.ArchiveAsync(agentId, CancellationToken.None);
        Assert.Equal(AgentOutcome.Conflict, blocked.Outcome);
        Assert.Equal("agent-has-active-tasks", blocked.ErrorCode);

        task.Status = TaskStatus.Succeeded;
        task.CompletedAtUtc = Now;
        await context.SaveChangesAsync();

        var archived = await service.ArchiveAsync(agentId, CancellationToken.None);
        Assert.Equal(AgentOutcome.Ok, archived.Outcome);
        Assert.NotNull(archived.Agent!.ArchivedAtUtc);
        Assert.False(archived.Agent.IsEnabled);

        var updateArchived = await service.UpdateAsync(
            agentId,
            new UpdateAgentRequest { DisplayName = "Nope", Role = "coder", PermissionProfile = "read-only" },
            CancellationToken.None);
        Assert.Equal(AgentOutcome.Conflict, updateArchived.Outcome);
        Assert.Equal("agent-archived", updateArchived.ErrorCode);

        var enableArchived = await service.EnableAsync(agentId, CancellationToken.None);
        Assert.Equal(AgentOutcome.Conflict, enableArchived.Outcome);

        var defaultList = await service.ListAsync(project.Id, null, false, 10, 0, CancellationToken.None);
        Assert.DoesNotContain(defaultList.Items, item => item.Id == agentId);

        var archivedList = await service.ListAsync(project.Id, null, true, 10, 0, CancellationToken.None);
        Assert.Contains(archivedList.Items, item => item.Id == agentId);
    }

    [SkippableFact]
    public async Task Code_is_unique_per_project_but_reusable_across_projects()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var first = await SeedProjectAsync(context);
        var second = await SeedProjectAsync(context);
        var service = CreateService(context);

        var request = new CreateAgentRequest
        {
            ProjectId = first.Id,
            Code = "shared-code",
            DisplayName = "Shared",
            Role = "coder",
            PermissionProfile = "read-only"
        };

        Assert.Equal(AgentOutcome.Ok, (await service.CreateAsync(request, CancellationToken.None)).Outcome);

        var sameProject = await service.CreateAsync(
            new CreateAgentRequest
            {
                ProjectId = first.Id,
                Code = "shared-code",
                DisplayName = "Shared 2",
                Role = "coder",
                PermissionProfile = "read-only"
            },
            CancellationToken.None);
        Assert.Equal(AgentOutcome.Conflict, sameProject.Outcome);

        var otherProject = await service.CreateAsync(
            new CreateAgentRequest
            {
                ProjectId = second.Id,
                Code = "shared-code",
                DisplayName = "Shared 3",
                Role = "coder",
                PermissionProfile = "read-only"
            },
            CancellationToken.None);
        Assert.Equal(AgentOutcome.Ok, otherProject.Outcome);
    }

    private static AgentService CreateService(AgentOSDbContext context) => new(
        new AgentRepository(context),
        TimeProvider.System);

    private static async Task<Project> SeedProjectAsync(AgentOSDbContext context)
    {
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Agent Registry",
            Slug = $"agents-{Guid.NewGuid():N}"[..20],
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
