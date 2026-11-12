using AgentOS.Api.Reviews;

namespace AgentOS.Api.Tests;

public sealed class ReviewChunkingTests
{
    [Fact]
    public void Large_diff_is_split_into_bounded_chunks()
    {
        var diff = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 8_000).Select(index => $"+ line {index:D5} some review content"));

        var chunks = ApiReviewAgent.SplitDiff(diff);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 60_000));
    }

    [Fact]
    public void Small_diff_stays_in_one_chunk()
    {
        var chunks = ApiReviewAgent.SplitDiff("+ one line");

        Assert.Single(chunks);
    }
}
