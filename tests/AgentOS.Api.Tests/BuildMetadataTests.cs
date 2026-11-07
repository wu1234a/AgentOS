using AgentOS.Api;

namespace AgentOS.Api.Tests;

public sealed class BuildMetadataTests
{
    [Fact]
    public void Version_info_has_stable_non_empty_fields()
    {
        var info = BuildMetadata.Get();

        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.False(string.IsNullOrWhiteSpace(info.GitCommit));
        Assert.False(string.IsNullOrWhiteSpace(info.BuildTimeUtc));
        Assert.False(string.IsNullOrWhiteSpace(info.Environment));
    }
}
