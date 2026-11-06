using AgentOS.Application.Tasks;
using AgentOS.Domain.Tasks;

namespace AgentOS.Api.Tests;

public sealed class LeaseTokenServiceTests
{
    private readonly LeaseTokenService _service = new();

    [Fact]
    public void Tokens_are_random_and_hashed_as_sha256_hex()
    {
        var first = _service.Create();
        var second = _service.Create();

        Assert.NotEqual(first.Token, second.Token);
        Assert.Equal(64, first.Hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", first.Hash);
        Assert.NotEqual(first.Token, first.Hash);
    }

    [Fact]
    public void Matches_accepts_the_original_token_and_rejects_others()
    {
        var (token, hash) = _service.Create();

        Assert.True(LeaseTokenService.Matches(hash, token));
        Assert.False(LeaseTokenService.Matches(hash, token + "x"));
        Assert.False(LeaseTokenService.Matches(null, token));
        Assert.False(LeaseTokenService.Matches(hash, null));
    }
}

public sealed class TaskBackoffTests
{
    private static readonly TaskQueueOptions Options = new()
    {
        BackoffBaseSeconds = 30,
        BackoffMaxSeconds = 900
    };

    [Fact]
    public void Backoff_grows_with_attempts()
    {
        var now = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        var taskId = Guid.Parse("0199c0de-0000-7000-8000-000000000001");

        var first = TaskBackoff.Compute(now, 1, Options, taskId);
        var second = TaskBackoff.Compute(now, 2, Options, taskId);
        var third = TaskBackoff.Compute(now, 3, Options, taskId);

        Assert.True(first < second);
        Assert.True(second < third);
    }

    [Fact]
    public void Backoff_is_capped()
    {
        var now = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        var taskId = Guid.Parse("0199c0de-0000-7000-8000-000000000002");

        var delay = TaskBackoff.Compute(now, 20, Options, taskId) - now;

        Assert.InRange(delay.TotalSeconds, Options.BackoffMaxSeconds, Options.BackoffMaxSeconds + 30);
    }

    [Fact]
    public void Jitter_stays_below_thirty_seconds()
    {
        var now = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);

        for (var index = 0; index < 10; index++)
        {
            var taskId = Guid.NewGuid();
            var delay = TaskBackoff.Compute(now, 1, Options, taskId) - now;
            Assert.InRange(delay.TotalSeconds, 30, 60);
        }
    }
}
