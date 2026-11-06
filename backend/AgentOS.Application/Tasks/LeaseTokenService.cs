using System.Security.Cryptography;
using System.Text;

namespace AgentOS.Application.Tasks;

/// <summary>
/// Creates and verifies lease tokens. Tokens are 256-bit cryptographically secure random values
/// rendered as Base64Url; the database only stores their SHA-256 hash.
/// </summary>
public sealed class LeaseTokenService
{
    private const int TokenBytes = 32;

    /// <summary>Creates a new random token and its lowercase-hex SHA-256 hash.</summary>
    public (string Token, string Hash) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        var token = Base64UrlEncode(bytes);
        return (token, Hash(token));
    }

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Constant-time comparison of a stored hash with the hash of a presented token.</summary>
    public static bool Matches(string? storedHash, string? presentedToken)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(presentedToken))
        {
            return false;
        }

        var presentedHash = Hash(presentedToken);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(storedHash),
            Encoding.ASCII.GetBytes(presentedHash));
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
