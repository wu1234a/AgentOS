namespace AgentOS.Application.Tasks;

/// <summary>
/// Exponential retry backoff with a cap and a small deterministic jitter, so recovered tasks
/// do not all become due in the same instant.
/// </summary>
public static class TaskBackoff
{
    private const int MaxExponent = 10;

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

        // Deterministic jitter in [0, 30) seconds derived from the task id.
        var jitter = taskId.ToByteArray()[0] % 30;

        return nowUtc.AddSeconds(seconds + jitter);
    }
}
