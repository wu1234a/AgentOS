using System.Security.Cryptography;

namespace AgentOS.Application.Tasks;

/// <summary>
/// Exponential retry backoff with a cap and a small deterministic jitter, so recovered tasks
/// do not all become due in the same instant.
/// </summary>
public static class TaskBackoff
{
    private const int MaxExponent = 10;
    private const int JitterSeconds = 30;

    public static DateTime Compute(
        DateTime nowUtc,
        int attemptCount,
        TaskQueueOptions options,
        Guid taskId)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, MaxExponent);
        var seconds = Math.Min(
            options.BackoffMaxSeconds,
            options.BackoffBaseSeconds * Math.Pow(2, exponent));

        // UUIDv7 puts the timestamp in the leading bytes, so a byte of the id itself is not
        // random for tasks created in a burst. Hash the whole id and use the digest instead.
        var digest = SHA256.HashData(taskId.ToByteArray());
        var jitter = (int)(BitConverter.ToUInt32(digest, 0) % JitterSeconds);

        return nowUtc.AddSeconds(seconds + jitter);
    }
}
