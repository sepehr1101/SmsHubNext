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
}
