using System.Text.Json;
using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Rules;

namespace AgentOS.Api.Tests;

public sealed class RuleValidatorTests
{
    private readonly FakeRuleReferenceLookup _lookup = FakeRuleReferenceLookup.Default();
    private readonly RuleValidator _validator;

    public RuleValidatorTests()
    {
        _validator = new RuleValidator(_lookup);
    }

    [Fact]
    public async Task Valid_rule_is_normalized_and_resolves_references()
    {
        var result = await _validator.ValidateAsync(Valid(), requireEnabledTargets: true, CancellationToken.None);

        Assert.True(result.IsValid);
        var values = result.Values!;
        Assert.Equal("3derp", values.ProjectSlug);
        Assert.Equal("planner", values.AgentCode);
        Assert.Equal("strong-reasoning", values.ModelProfileCode);
        Assert.Contains("development_task", values.MatchJson);
        Assert.Equal(1800, values.TimeoutSeconds);
    }

    [Fact]
    public async Task Unknown_agent_is_rejected()
    {
        var request = Valid(agentCode: "missing");

        var result = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);

        Assert.Contains("agentCode", result.Errors.Keys);
    }

    [Fact]
    public async Task Disabled_agent_blocks_enabling_but_allows_a_draft()
    {
        var request = Valid(agentCode: "disabled-agent", isEnabled: true);
        var enabledResult = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);
        Assert.Contains("agentCode", enabledResult.Errors.Keys);

        var draft = Valid(agentCode: "disabled-agent", isEnabled: false);
        var draftResult = await _validator.ValidateAsync(draft, requireEnabledTargets: false, CancellationToken.None);
        Assert.True(draftResult.IsValid);
    }

    [Fact]
    public async Task Invalid_match_json_is_rejected()
    {
        var request = Valid(matchJson: """{"unknownProperty": true}""");

        var result = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);

        Assert.Contains("matchJson", result.Errors.Keys);
    }

    [Fact]
    public async Task Non_object_match_json_is_rejected()
    {
        var request = Valid(matchJson: "[1,2]");

        var result = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);

        Assert.Contains("matchJson", result.Errors.Keys);
    }

    [Fact]
    public async Task Dispatch_key_with_separator_is_rejected()
    {
        var request = Valid(dispatchJson: """{"dispatchKey":"a:b"}""");

        var result = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);

        Assert.Contains("dispatchJson", result.Errors.Keys);
    }

    [Fact]
    public async Task Invalid_limits_are_rejected()
    {
        var request = Valid(maxParallel: 0, maxAttempts: 0, timeoutSeconds: 1);

        var result = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);

        Assert.Contains("maxParallel", result.Errors.Keys);
        Assert.Contains("maxAttempts", result.Errors.Keys);
        Assert.Contains("timeoutSeconds", result.Errors.Keys);
    }

    [Fact]
    public async Task Disabled_project_is_rejected()
    {
        var request = Valid(project: "disabled-demo");

        var result = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);

        Assert.Contains("project", result.Errors.Keys);
    }

    [Fact]
    public async Task Disabled_skill_and_profile_are_rejected_when_enabling()
    {
        var request = Valid(skillCode: "disabled-skill", modelProfileCode: "disabled-profile");

        var result = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);

        Assert.Contains("skillCode", result.Errors.Keys);
        Assert.Contains("modelProfileCode", result.Errors.Keys);
    }

    [Fact]
    public async Task Null_condition_arrays_are_treated_as_empty()
    {
        var request = Valid(matchJson: """{"subjectTypes":null,"priorities":null}""");

        var result = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.SelectMany(pair => pair.Value)));
    }

    [Fact]
    public async Task Source_pattern_longer_than_the_column_is_rejected()
    {
        var request = Valid(sourcePattern: new string('a', 65));

        var result = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);

        Assert.Contains("sourcePattern", result.Errors.Keys);
    }

    [Fact]
    public async Task Dispatch_settings_are_validated()
    {
        var request = Valid(dispatchJson: """{"dispatchKey":"second","priority":"urgent"}""");

        var result = await _validator.ValidateAsync(request, requireEnabledTargets: true, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("urgent", JsonDocument.Parse(result.Values!.DispatchJson).RootElement.GetProperty("priority").GetString());
    }

    private static SaveRuleRequest Valid(
        string name = "3DERP development planning",
        string? project = "3derp",
        string sourcePattern = "3derp",
        string eventTypePattern = "development.task.*",
        string agentCode = "planner",
        string? skillCode = "3derp-plan-development",
        string? modelProfileCode = "strong-reasoning",
        string? matchJson = """{"subjectTypes":["development_task"]}""",
        string? dispatchJson = """{"dispatchKey":"default","priority":"normal"}""",
        int priority = 10,
        int maxParallel = 1,
        int maxAttempts = 2,
        int timeoutSeconds = 1800,
        bool isEnabled = true) => new()
    {
        Name = name,
        Project = project,
        SourcePattern = sourcePattern,
        EventTypePattern = eventTypePattern,
        AgentCode = agentCode,
        SkillCode = skillCode,
        ModelProfileCode = modelProfileCode,
        MatchJson = matchJson is null ? null : JsonDocument.Parse(matchJson).RootElement.Clone(),
        DispatchJson = dispatchJson is null ? null : JsonDocument.Parse(dispatchJson).RootElement.Clone(),
        Priority = priority,
        MaxParallel = maxParallel,
        MaxAttempts = maxAttempts,
        TimeoutSeconds = timeoutSeconds,
        NotificationPolicy = "failure-or-approval",
        IsEnabled = isEnabled
    };
}
