using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Agents;
using AgentOS.Application.ModelProfiles;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Agents;
using AgentOS.Domain.ModelProfiles;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Rules;
using AgentOS.Domain.Workers;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Agents;
using AgentOS.Infrastructure.ModelProfiles;
using AgentOS.Infrastructure.Tasks;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskStatus = AgentOS.Domain.Tasks.TaskStatus;

namespace AgentOS.Api.Tests;

/// <summary>AOS-010 acceptance tests for model profiles and resolution.</summary>
[Collection("SqlServer")]
public sealed class SqlModelProfileTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 11, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Crud_validates_secret_references_and_configuration()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var service = CreateService(context);
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var created = await service.CreateAsync(
            new CreateModelProfileRequest
            {
                Code = $"codex-{suffix}",
                Provider = "codex-cli",
                Model = "gpt-5-codex",
                ExecutionMode = "cli",
                ConfigurationJson = """{"reasoning":"medium"}""",
                SecretReference = "OPENAI_API_KEY",
                TimeoutSeconds = 900
            },
            CancellationToken.None);

        Assert.Equal(ModelProfileOutcome.Ok, created.Outcome);
        Assert.True(created.Profile!.IsEnabled);
        Assert.Equal("OPENAI_API_KEY", created.Profile.SecretReference);

        var duplicate = await service.CreateAsync(
            new CreateModelProfileRequest
            {
                Code = $"codex-{suffix}",
                Provider = "codex-cli",
                Model = "gpt-5-codex",
                ExecutionMode = "cli"
            },
            CancellationToken.None);
        Assert.Equal(ModelProfileOutcome.Conflict, duplicate.Outcome);
        Assert.Equal("model-profile-code-conflict", duplicate.ErrorCode);

        var secretValue = await service.CreateAsync(
            new CreateModelProfileRequest
            {
                Code = $"bad-secret-{suffix}",
                Provider = "openai",
                Model = "gpt-5",
                ExecutionMode = "api",
                SecretReference = "sk-super-secret-value"
            },
            CancellationToken.None);
        Assert.Equal(ModelProfileOutcome.ValidationFailed, secretValue.Outcome);
        Assert.Contains("secretReference", secretValue.Errors.Keys);

        var secretInConfig = await service.CreateAsync(
            new CreateModelProfileRequest
            {
                Code = $"bad-config-{suffix}",
                Provider = "openai",
                Model = "gpt-5",
                ExecutionMode = "api",
                ConfigurationJson = """{"api_key":"sk-123"}"""
            },
            CancellationToken.None);
        Assert.Equal(ModelProfileOutcome.ValidationFailed, secretInConfig.Outcome);
        Assert.Contains("configurationJson", secretInConfig.Errors.Keys);

        var nestedSecret = await service.CreateAsync(
            new CreateModelProfileRequest
            {
                Code = $"nested-secret-{suffix}",
                Provider = "openai",
                Model = "gpt-5",
                ExecutionMode = "api",
                ConfigurationJson = """{"credentials":{"api_key":"sk-123"}}"""
            },
            CancellationToken.None);
        Assert.Equal(ModelProfileOutcome.ValidationFailed, nestedSecret.Outcome);
        Assert.Contains("configurationJson", nestedSecret.Errors.Keys);

        var nonObject = await service.CreateAsync(
            new CreateModelProfileRequest
            {
                Code = $"bad-json-{suffix}",
                Provider = "openai",
                Model = "gpt-5",
                ExecutionMode = "api",
                ConfigurationJson = "[1,2,3]"
            },
            CancellationToken.None);
        Assert.Equal(ModelProfileOutcome.ValidationFailed, nonObject.Outcome);

        var disabled = await service.DisableAsync(created.Profile.Id, CancellationToken.None);
        Assert.False(disabled.Profile!.IsEnabled);

        var enabled = await service.EnableAsync(created.Profile.Id, CancellationToken.None);
        Assert.True(enabled.Profile!.IsEnabled);
    }

    [SkippableFact]
    public async Task Resolution_prefers_task_then_rule_then_agent_and_skips_disabled()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var service = CreateService(context);

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var taskCode = $"task-model-{suffix}";
        var ruleCode = $"rule-model-{suffix}";
        var agentCode = $"agent-model-{suffix}";
        var taskProfile = await CreateProfileAsync(service, taskCode);
        var ruleProfile = await CreateProfileAsync(service, ruleCode);
        var agentProfile = await CreateProfileAsync(service, agentCode);

        var project = await SeedProjectAsync(context);
        var agent = new Agent
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Code = $"agent-{Guid.NewGuid():N}"[..20],
            DisplayName = "Model Agent",
            Role = "coder",
            PermissionProfile = "read-only",
            DefaultModelProfileId = agentProfile.Profile!.Id,
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Agents.Add(agent);

        var rule = new AutomationRule
        {
            Id = Guid.CreateVersion7(),
            Name = "Model rule",
            ProjectId = project.Id,
            AgentId = agent.Id,
            ModelProfileId = ruleProfile.Profile!.Id,
            IsEnabled = true,
            Version = 1,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.AutomationRules.Add(rule);

        var task = new AutomationTask
        {
            Id = Guid.CreateVersion7(),
            TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
            ProjectId = project.Id,
            AgentId = agent.Id,
            RuleId = rule.Id,
            ModelProfileId = taskProfile.Profile!.Id,
            LogicalTaskKey = $"model-{Guid.NewGuid():N}",
            Status = TaskStatus.Queued,
            Priority = "normal",
            Title = "Model resolution",
            InputJson = "{}",
            AttemptCount = 0,
            MaxAttempts = 2,
            NextRunAtUtc = Now,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        var repository = new ModelProfileRepository(context);

        var fromTask = await repository.ResolveAsync(task.Id, null, null, CancellationToken.None);
        Assert.NotNull(fromTask);
        Assert.Equal(taskCode, fromTask!.Code);
        Assert.Equal("task", fromTask.Source);

        await service.DisableAsync(taskProfile.Profile.Id, CancellationToken.None);
        var fromRule = await repository.ResolveAsync(task.Id, null, null, CancellationToken.None);
        Assert.NotNull(fromRule);
        Assert.Equal(ruleCode, fromRule!.Code);
        Assert.Equal("rule", fromRule.Source);

        await service.DisableAsync(ruleProfile.Profile.Id, CancellationToken.None);

        var ruleOnly = await repository.ResolveAsync(null, rule.Id, null, CancellationToken.None);
        Assert.NotNull(ruleOnly);
        Assert.Equal(agentCode, ruleOnly!.Code);
        Assert.Equal("agent", ruleOnly.Source);

        var fromAgent = await repository.ResolveAsync(task.Id, null, null, CancellationToken.None);
        Assert.NotNull(fromAgent);
        Assert.Equal(agentCode, fromAgent!.Code);
        Assert.Equal("agent", fromAgent.Source);

        await service.DisableAsync(agentProfile.Profile.Id, CancellationToken.None);
        Assert.Null(await repository.ResolveAsync(task.Id, null, null, CancellationToken.None));
    }

    private static async Task<ModelProfileResult> CreateProfileAsync(ModelProfileService service, string code)
    {
        var result = await service.CreateAsync(
            new CreateModelProfileRequest
            {
                Code = code,
                Provider = "codex-cli",
                Model = "gpt-5-codex",
                ExecutionMode = "cli"
            },
            CancellationToken.None);

        Assert.Equal(ModelProfileOutcome.Ok, result.Outcome);
        return result;
    }

    [SkippableFact]
    public async Task Claim_attaches_the_resolved_model_profile()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        await using var context = NewContext(connectionString);
        var service = CreateService(context);
        var code = $"claim-model-{Guid.NewGuid().ToString("N")[..6]}";
        var profile = await CreateProfileAsync(service, code);

        var project = await SeedProjectAsync(context);
        var agent = new Agent
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Code = $"claim-agent-{Guid.NewGuid().ToString("N")[..6]}",
            DisplayName = "Claim Agent",
            Role = "coder",
            PermissionProfile = "read-only",
            DefaultModelProfileId = profile.Profile!.Id,
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Agents.Add(agent);

        var worker = new Worker
        {
            Id = Guid.CreateVersion7(),
            WorkerCode = $"worker-{Guid.NewGuid().ToString("N")[..6]}",
            DisplayName = "Claim Runner",
            HostName = "localhost",
            Platform = "Linux",
            Architecture = "x64",
            Version = "1.0.0",
            Status = WorkerStatus.Online,
            LastHeartbeatAtUtc = Now,
            RegisteredAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Workers.Add(worker);

        var task = new AutomationTask
        {
            Id = Guid.CreateVersion7(),
            TaskCode = $"TASK-{Guid.NewGuid():N}".ToUpperInvariant(),
            ProjectId = project.Id,
            AgentId = agent.Id,
            LogicalTaskKey = $"model-claim-{Guid.NewGuid():N}",
            Status = TaskStatus.Queued,
            Priority = "normal",
            Title = "Claim model",
            InputJson = "{}",
            AttemptCount = 0,
            MaxAttempts = 2,
            NextRunAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        var claimer = new TaskClaimer(
            context,
            new LeaseTokenService(),
            TimeProvider.System,
            Options.Create(new TaskQueueOptions { ClaimBatchLimit = 10 }),
            new FakeSkillContentReader(),
            new ModelProfileRepository(context),
            NullLogger<TaskClaimer>.Instance);

        var claimed = await claimer.ClaimAsync(worker.Id, 1, CancellationToken.None);
        var item = Assert.Single(claimed);
        Assert.NotNull(item.ModelProfile);
        Assert.Equal(code, item.ModelProfile!.Code);
        Assert.Equal("agent", item.ModelProfile.Source);
    }

    private static ModelProfileService CreateService(AgentOSDbContext context) => new(
        new ModelProfileRepository(context),
        TimeProvider.System);

    private static async Task<Project> SeedProjectAsync(AgentOSDbContext context)
    {
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Model Router",
            Slug = $"models-{Guid.NewGuid():N}"[..20],
            IsEnabled = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project;
    }

    private static async Task ResetQueueAsync(string connectionString)
    {
        await using var context = NewContext(connectionString);
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM [CodingRunEvents]; DELETE FROM [CodingRuns]; DELETE FROM [TaskLogs]; DELETE FROM [UsageRecords]; DELETE FROM [Artifacts]; DELETE FROM [TaskAttempts]; DELETE FROM [Tasks];");
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
