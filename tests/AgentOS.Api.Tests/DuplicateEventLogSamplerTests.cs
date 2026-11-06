using AgentOS.Api.Tests.Fakes;
using AgentOS.Application.Events;

namespace AgentOS.Api.Tests;

public sealed class DuplicateEventLogSamplerTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void First_occurrence_is_always_logged()
    {
        var sampler = CreateSampler();

        var decision = sampler.Observe("3derp", "3derp:DEV-1");

        Assert.True(decision.ShouldLog);
        Assert.Equal(1, decision.OccurrencesInWindow);
        Assert.Equal(0, decision.SuppressedSinceLastLog);
    }

    [Fact]
    public void Repeated_occurrences_within_the_window_are_suppressed_until_the_sample_point()
    {
        var sampler = CreateSampler();
        sampler.Observe("3derp", "3derp:DEV-1");

        var second = sampler.Observe("3derp", "3derp:DEV-1");
        Assert.False(second.ShouldLog);
        Assert.Equal(2, second.OccurrencesInWindow);

        for (var i = 3; i < DuplicateEventLogSampler.LogEveryNth; i++)
        {
            Assert.False(sampler.Observe("3derp", "3derp:DEV-1").ShouldLog);
        }

        var samplePoint = sampler.Observe("3derp", "3derp:DEV-1");
        Assert.True(samplePoint.ShouldLog);
        Assert.Equal(DuplicateEventLogSampler.LogEveryNth, samplePoint.OccurrencesInWindow);
        Assert.Equal(DuplicateEventLogSampler.LogEveryNth - 1, samplePoint.SuppressedSinceLastLog);
    }

    [Fact]
    public void A_new_window_reports_how_many_occurrences_were_suppressed()
    {
        var sampler = CreateSampler();
        for (var i = 0; i < 5; i++)
        {
            sampler.Observe("github", "github:repo:push:1");
        }

        _time.Advance(DuplicateEventLogSampler.Window);

        var decision = sampler.Observe("github", "github:repo:push:1");

        Assert.True(decision.ShouldLog);
        Assert.Equal(1, decision.OccurrencesInWindow);
        Assert.Equal(4, decision.SuppressedSinceLastLog);
    }

    [Fact]
    public void Keys_are_sampled_independently()
    {
        var sampler = CreateSampler();
        sampler.Observe("3derp", "a");

        var other = sampler.Observe("3derp", "b");

        Assert.True(other.ShouldLog);
        Assert.Equal(1, other.OccurrencesInWindow);
    }

    [Fact]
    public void Conflict_traffic_is_sampled_separately_from_duplicate_traffic()
    {
        var sampler = CreateSampler();
        sampler.Observe("3derp", "3derp:DEV-1");

        var conflict = sampler.Observe("3derp", "3derp:DEV-1", isConflict: true);

        // The first conflict of the window is always auditable, even though the same key already
        // produced a duplicate log entry in the same window.
        Assert.True(conflict.ShouldLog);
        Assert.Equal(1, conflict.OccurrencesInWindow);
    }

    [Fact]
    public void A_flood_of_distinct_keys_is_hard_capped()
    {
        var sampler = CreateSampler();
        for (var i = 0; i < 10_000; i++)
        {
            sampler.Observe("source", $"key-{i}");
        }

        Assert.True(
            sampler.TrackedKeyCount <= DuplicateEventLogSampler.MaxTrackedKeys,
            $"tracked keys: {sampler.TrackedKeyCount}");
    }

    [Fact]
    public void Expired_windows_are_evicted_before_live_ones()
    {
        var sampler = CreateSampler();
        for (var i = 0; i < DuplicateEventLogSampler.MaxTrackedKeys; i++)
        {
            sampler.Observe("source", $"old-{i}");
        }

        Assert.Equal(DuplicateEventLogSampler.MaxTrackedKeys, sampler.TrackedKeyCount);

        _time.Advance(DuplicateEventLogSampler.Window * 6);
        sampler.Observe("source", "fresh");

        Assert.Equal(1, sampler.TrackedKeyCount);
    }

    private DuplicateEventLogSampler CreateSampler() => new(_time);
}
