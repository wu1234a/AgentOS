using System.Security.Cryptography;
using System.Text;

namespace AgentOS.Application.Security;

/// <summary>Creates and verifies machine client secrets. Only hashes are persisted.</summary>
public static class ClientSecretHasher
{
    private const int SecretBytes = 32;

    public static string CreateSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    public static bool Matches(string? storedHash, string? presentedSecret)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(presentedSecret))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(storedHash),
            Encoding.ASCII.GetBytes(Hash(presentedSecret)));
    }

    /// <summary>Constant-time comparison for a shared bootstrap key.</summary>
    public static bool FixedTimeEquals(string? expected, string? presented)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(presented));
    }
}
