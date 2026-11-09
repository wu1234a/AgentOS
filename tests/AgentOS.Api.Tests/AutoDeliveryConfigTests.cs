using System.Text.Json;
using AgentOS.Runner.Delivery;

namespace AgentOS.Api.Tests;

public sealed class AutoDeliveryConfigTests
{
    [Fact]
    public void Missing_delivery_block_means_disabled()
    {
        Assert.True(AutoDeliveryConfig.TryParse("{}", "TASK-1", out var config, out var error));
        Assert.Null(config);
        Assert.Null(error);
    }

    [Fact]
    public void Disabled_delivery_block_means_disabled()
    {
        Assert.True(AutoDeliveryConfig.TryParse(
            """{"delivery":{"enabled":false}}""",
            "TASK-1",
            out var config,
            out var error));
        Assert.Null(config);
        Assert.Null(error);
    }

    [Fact]
    public void Valid_delivery_config_is_parsed_with_defaults()
    {
        Assert.True(AutoDeliveryConfig.TryParse(
            """{"delivery":{"enabled":true,"repository":"owner/repo"}}""",
            "TASK-ABC 1",
            out var config,
            out var error));

        Assert.NotNull(config);
        Assert.Null(error);
        Assert.Equal("owner/repo", config.Repository);
        Assert.Equal("main", config.BaseBranch);
        Assert.Equal("aos/task-abc-1", config.Branch);
        Assert.Equal("chore: apply TASK-ABC 1", config.CommitMessage);
        Assert.Equal("AGENTOS_GITHUB_TOKEN", config.TokenEnvironmentVariable);
        Assert.True(config.RequirePullRequest);
    }

    [Fact]
    public void Branch_equal_to_base_is_rejected()
    {
        Assert.False(AutoDeliveryConfig.TryParse(
            """{"delivery":{"enabled":true,"repository":"owner/repo","baseBranch":"main","branch":"main"}}""",
            "TASK-1",
            out _,
            out var error));
        Assert.Contains("must differ", error);
    }

    [Theory]
    [InlineData("bad..branch")]
    [InlineData("-bad")]
    [InlineData("bad branch")]
    [InlineData("bad\\branch")]
    [InlineData("bad.lock")]
    [InlineData("bad/")]
    public void Invalid_branch_is_rejected(string branch)
    {
        var input = JsonSerializer.Serialize(new
        {
            delivery = new { enabled = true, repository = "owner/repo", branch }
        });
        Assert.False(AutoDeliveryConfig.TryParse(input, "TASK-1", out _, out var error));
        Assert.Contains("branch", error);
    }

    [Fact]
    public void Pull_request_requires_repository()
    {
        Assert.False(AutoDeliveryConfig.TryParse(
            """{"delivery":{"enabled":true,"requirePullRequest":true}}""",
            "TASK-1",
            out _,
            out var error));
        Assert.Contains("repository", error);
    }

    [Fact]
    public void Invalid_token_variable_is_rejected()
    {
        Assert.False(AutoDeliveryConfig.TryParse(
            """{"delivery":{"enabled":true,"repository":"owner/repo","tokenEnvironmentVariable":"my-token"}}""",
            "TASK-1",
            out _,
            out var error));
        Assert.Contains("tokenEnvironmentVariable", error);
    }
}
