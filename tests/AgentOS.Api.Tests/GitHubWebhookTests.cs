using System.Security.Cryptography;
using System.Text;
using AgentOS.Api.Endpoints;

namespace AgentOS.Api.Tests;

public sealed class GitHubWebhookTests
{
    [Fact]
    public void Verifies_sha256_signature_and_rejects_tampering()
    {
        var body = Encoding.UTF8.GetBytes("""{"repository":{"full_name":"owner/repo"}}""");
        const string secret = "test-secret";
        var signature = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

        Assert.True(GitHubWebhookEndpoints.VerifySignature(body, secret, signature));
        Assert.False(GitHubWebhookEndpoints.VerifySignature(
            Encoding.UTF8.GetBytes("""{"repository":{"full_name":"evil/repo"}}"""),
            secret,
            signature));
        Assert.False(GitHubWebhookEndpoints.VerifySignature(body, secret, "sha1=abc"));
    }
}
