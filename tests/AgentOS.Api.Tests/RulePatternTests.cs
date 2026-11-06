using AgentOS.Application.Rules;

namespace AgentOS.Api.Tests;

public sealed class RulePatternTests
{
    [Theory]
    [InlineData(null, "anything", true)]
    [InlineData("", "anything", true)]
    [InlineData("*", "anything", true)]
    [InlineData("3derp", "3derp", true)]
    [InlineData("3derp", "3DERP", true)]
    [InlineData("3derp", "4derp", false)]
    [InlineData("github.*", "github.issue.opened", true)]
    [InlineData("github.*", "3derp.issue.opened", false)]
    [InlineData("*.task.created", "development.task.created", true)]
    [InlineData("development.*.created", "development.task.created", true)]
    [InlineData("development.*.created", "development.task.updated", false)]
    public void Glob_matching_is_predictable(string? pattern, string value, bool expected)
        => Assert.Equal(expected, RulePattern.IsMatch(pattern, value));

    [Theory]
    [InlineData("a?b")]
    public void Question_mark_is_rejected(string pattern)
    {
        Assert.False(RulePattern.IsValid(pattern, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Long_patterns_are_rejected()
    {
        Assert.False(RulePattern.IsValid(new string('a', 121), out _));
        Assert.True(RulePattern.IsValid(new string('a', 120), out _));
    }
}
