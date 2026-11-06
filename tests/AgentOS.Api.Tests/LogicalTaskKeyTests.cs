using AgentOS.Domain.Tasks;

namespace AgentOS.Api.Tests;

public sealed class LogicalTaskKeyTests
{
    private static readonly Guid EventId = Guid.Parse("0199c0de-0000-7000-8000-000000000001");
    private static readonly Guid RuleId = Guid.Parse("0199c0de-0000-7000-8000-000000000002");

    [Fact]
    public void Key_is_deterministic()
    {
        var first = LogicalTaskKey.Create(EventId, RuleId, 3, "default");
        var second = LogicalTaskKey.Create(EventId, RuleId, 3, "default");

        Assert.Equal(first, second);
        Assert.Equal($"{EventId:N}:{RuleId:N}:3:default", first);
    }

    [Fact]
    public void Each_component_changes_the_key()
    {
        var baseline = LogicalTaskKey.Create(EventId, RuleId, 1, "default");

        Assert.NotEqual(baseline, LogicalTaskKey.Create(Guid.CreateVersion7(), RuleId, 1, "default"));
        Assert.NotEqual(baseline, LogicalTaskKey.Create(EventId, Guid.CreateVersion7(), 1, "default"));
        Assert.NotEqual(baseline, LogicalTaskKey.Create(EventId, RuleId, 2, "default"));
        Assert.NotEqual(baseline, LogicalTaskKey.Create(EventId, RuleId, 1, "secondary"));
    }

    [Fact]
    public void Dispatch_key_is_trimmed()
    {
        Assert.Equal(
            LogicalTaskKey.Create(EventId, RuleId, 1, "default"),
            LogicalTaskKey.Create(EventId, RuleId, 1, "  default  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a:b")]
    public void Invalid_dispatch_keys_are_rejected(string dispatchKey)
    {
        Assert.False(LogicalTaskKey.IsValidDispatchKey(dispatchKey, out _));
        Assert.Throws<ArgumentException>(() => LogicalTaskKey.Create(EventId, RuleId, 1, dispatchKey));
    }

    [Fact]
    public void Overlong_dispatch_keys_are_rejected()
    {
        var tooLong = new string('x', LogicalTaskKey.MaxDispatchKeyLength + 1);

        Assert.Throws<ArgumentException>(() => LogicalTaskKey.Create(EventId, RuleId, 1, tooLong));
    }

    [Fact]
    public void Rule_version_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogicalTaskKey.Create(EventId, RuleId, 0, "default"));
    }

    [Fact]
    public void Key_stays_within_the_database_column()
    {
        var key = LogicalTaskKey.Create(
            EventId,
            RuleId,
            int.MaxValue,
            new string('d', LogicalTaskKey.MaxDispatchKeyLength));

        Assert.True(key.Length <= LogicalTaskKey.MaxLength, $"length: {key.Length}");
    }
}
