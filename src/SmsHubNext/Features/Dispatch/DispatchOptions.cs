namespace SmsHubNext.Features.Dispatch;

/// <summary>Tuning for the background dispatch worker (bound from the <c>Dispatch</c> config section).</summary>
public sealed class DispatchOptions
{
    public const string SectionName = "Dispatch";

    /// <summary>How long to idle when there is no claimable batch before polling again.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a <c>Held</c> batch waits before it is eligible to be resumed.</summary>
    public TimeSpan HoldRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a batch may sit in <c>Dispatching</c> before it is presumed abandoned by a
    /// crashed/recycled worker and reclaimed. Reprocessing is safe: only still-<c>Queued</c>
    /// messages are re-sent (guarded, idempotent transitions).
    /// </summary>
    public TimeSpan DispatchLeaseTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum transient dispatch attempts before the batch is marked failed.</summary>
    public int MaxDispatchAttempts { get; init; } = 5;

    /// <summary>
    /// Retry delays, in seconds, by dispatch attempt. Attempts beyond the configured list use the
    /// last delay. Defaults: 30 seconds, 2 minutes, 5 minutes, then 15 minutes.
    /// </summary>
    public int[] RetryBackoffSeconds { get; init; } = [30, 120, 300, 900];

    public TimeSpan RetryDelayForAttempt(int dispatchAttemptCount)
    {
        if (RetryBackoffSeconds.Length == 0)
            return TimeSpan.Zero;

        int index = Math.Clamp(dispatchAttemptCount - 1, 0, RetryBackoffSeconds.Length - 1);
        return TimeSpan.FromSeconds(RetryBackoffSeconds[index]);
    }
}
