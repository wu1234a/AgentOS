using System.Collections.Concurrent;

namespace AgentOS.Application.Events;

/// <summary>Decision produced by <see cref="DuplicateEventLogSampler"/>.</summary>
/// <param name="ShouldLog">Whether this occurrence should be written to the log.</param>
/// <param name="OccurrencesInWindow">Occurrences of this key in the current window, including this one.</param>
/// <param name="SuppressedSinceLastLog">Occurrences since the previous log entry for this key.</param>
public readonly record struct DuplicateLogDecision(
    bool ShouldLog,
    long OccurrencesInWindow,
    long SuppressedSinceLastLog);

/// <summary>
/// Rate-limits duplicate/conflict log entries per deduplication key so that a sender stuck in
/// a retry loop cannot flood the log. The first occurrence in a window is always logged; within
/// a window only every <see cref="LogEveryNth"/> occurrence is logged, and the first occurrence
/// of the next window reports how many entries were suppressed.
/// </summary>
/// <remarks>
/// Duplicate and conflict traffic are counted separately (<c>channel</c>), so a key that first
/// retries many identical submissions and then switches to conflicting content still produces an
/// auditable warning for the first conflict in each window.
/// The tracked-key dictionary is hard-capped at <see cref="MaxTrackedKeys"/>; once the cap is
/// exceeded it is trimmed (expired windows first, then the oldest windows) down to half the cap,
/// so a flood of distinct keys cannot grow memory without bound or make pruning run on every
/// request.
/// </remarks>
public sealed class DuplicateEventLogSampler
{
    /// <summary>Log one occurrence out of this many inside a window.</summary>
    public const int LogEveryNth = 100;

    /// <summary>Hard cap on the number of keys tracked at once.</summary>
    public const int MaxTrackedKeys = 4096;

    /// <summary>Length of the sampling window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private const int TrimTarget = MaxTrackedKeys / 2;
    private const int CleanupMultiplier = 5;
    private const string DuplicateChannel = "duplicate";
    private const string ConflictChannel = "conflict";

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private int _trimming;

    public DuplicateEventLogSampler(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>Records a duplicate occurrence and decides whether it should be logged.</summary>
    public DuplicateLogDecision Observe(string source, string dedupeKey)
        => Observe(source, dedupeKey, isConflict: false);

    /// <summary>Records a duplicate or conflict occurrence and decides whether it should be logged.</summary>
    public DuplicateLogDecision Observe(string source, string dedupeKey, bool isConflict)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupeKey);

        var now = _timeProvider.GetUtcNow();
        var key = string.Concat(
            isConflict ? ConflictChannel : DuplicateChannel,
            "\n",
            source,
            "\n",
            dedupeKey);
        var counter = _counters.GetOrAdd(key, static _ => new Counter());

        DuplicateLogDecision decision;
        lock (counter)
        {
            if (counter.Count == 0)
            {
                counter.WindowStartUtc = now;
                counter.Count = 1;
                decision = new DuplicateLogDecision(true, 1, 0);
            }
            else if (now - counter.WindowStartUtc >= Window)
            {
                var suppressed = counter.Count - 1;
                counter.WindowStartUtc = now;
                counter.Count = 1;
                decision = new DuplicateLogDecision(true, 1, suppressed);
            }
            else
            {
                counter.Count++;
                var shouldLog = counter.Count % LogEveryNth == 0;
                decision = new DuplicateLogDecision(shouldLog, counter.Count, shouldLog ? LogEveryNth - 1 : 0);
            }
        }

        if (_counters.Count > MaxTrackedKeys)
        {
            TryTrim(now);
        }

        return decision;
    }

    /// <summary>Number of keys currently tracked; exposed for tests and diagnostics.</summary>
    public int TrackedKeyCount => _counters.Count;

    private void TryTrim(DateTimeOffset now)
    {
        // Only one trim at a time; the rest of the requests keep flowing.
        if (Interlocked.CompareExchange(ref _trimming, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var cutoff = now - (Window * CleanupMultiplier);

            foreach (var pair in _counters)
            {
                if (pair.Value.WindowStartUtc < cutoff)
                {
                    _counters.TryRemove(pair);
                }
            }

            var toRemove = _counters.Count - TrimTarget;
            if (toRemove <= 0)
            {
                return;
            }

            foreach (var pair in _counters.OrderBy(entry => entry.Value.WindowStartUtc).Take(toRemove))
            {
                _counters.TryRemove(pair);
            }
        }
        finally
        {
            Volatile.Write(ref _trimming, 0);
        }
    }

    private sealed class Counter
    {
        public DateTimeOffset WindowStartUtc { get; set; }

        public long Count { get; set; }
    }
}
