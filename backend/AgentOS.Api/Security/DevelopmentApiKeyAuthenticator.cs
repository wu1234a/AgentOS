using System.Security.Cryptography;
using System.Text;
using AgentOS.Application.Security;

namespace AgentOS.Api.Security;

/// <summary>
/// Clearly marked development authenticator. It accepts the single configured
/// development key; production builds register the fail-closed authenticator instead.
/// </summary>
public sealed class DevelopmentApiKeyAuthenticator : IApiClientAuthenticator
{
    private readonly string? _apiKey;

    public DevelopmentApiKeyAuthenticator(IConfiguration configuration)
    {
        _apiKey = configuration["Authentication:DevelopmentApiKey"];
    }

    public ValueTask<ApiClientIdentity?> AuthenticateAsync(
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            return ValueTask.FromResult<ApiClientIdentity?>(null);
        }

        var expected = Encoding.UTF8.GetBytes(_apiKey);
        var actual = Encoding.UTF8.GetBytes(apiKey);
        var valid = expected.Length == actual.Length
                    && CryptographicOperations.FixedTimeEquals(expected, actual);

        return ValueTask.FromResult<ApiClientIdentity?>(
            valid ? new ApiClientIdentity("development", "Development") : null);
    }
}
