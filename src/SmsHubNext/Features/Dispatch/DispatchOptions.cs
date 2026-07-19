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
    /// Minimum time to wait before trusting a provider "no record" response for a message whose
    /// submit outcome is unknown. This avoids resending while the provider may still be indexing uid/mid.
    /// </summary>
    public TimeSpan MinAwaitingConfirmationAge { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Delay between confirmation lookups for messages with unknown submit outcome. These retries do
    /// not terminal-fail the batch because the conservative choice is "do not resend yet".
    /// </summary>
    public TimeSpan AwaitingConfirmationRetryDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maximum age for an unknown submit outcome. Past this window provider "not found" answers may
    /// no longer prove the message was never accepted, so the batch is held for manual review.
    /// </summary>
    public TimeSpan AwaitingConfirmationMaxAge { get; init; } = TimeSpan.FromHours(11);

    /// <summary>How many negative provider lookups are required before a message is considered safe to resend.</summary>
    public int RequiredNegativeConfirmations { get; init; } = 2;

    /// <summary>
    /// Retry delays, in seconds, by dispatch attempt. Attempts beyond the configured list use the
    /// last delay. Defaults: 30 seconds, 2 minutes, 5 minutes, then 15 minutes.
    /// </summary>
    public int[] RetryBackoffSeconds { get; init; } = [30, 120, 300, 900];

    public void Validate()
    {
        if (PollInterval <= TimeSpan.Zero)
            throw new InvalidOperationException($"{SectionName}:PollInterval must be positive.");

        if (HoldRetryDelay < TimeSpan.Zero)
            throw new InvalidOperationException($"{SectionName}:HoldRetryDelay cannot be negative.");

        if (DispatchLeaseTimeout < TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException(
                $"{SectionName}:DispatchLeaseTimeout must be at least one minute so an in-flight provider request is not reclaimed prematurely.");
        }

        if (MaxDispatchAttempts < 1)
            throw new InvalidOperationException($"{SectionName}:MaxDispatchAttempts must be positive.");

        if (MinAwaitingConfirmationAge <= TimeSpan.Zero)
            throw new InvalidOperationException($"{SectionName}:MinAwaitingConfirmationAge must be positive.");

        if (AwaitingConfirmationRetryDelay <= TimeSpan.Zero)
            throw new InvalidOperationException($"{SectionName}:AwaitingConfirmationRetryDelay must be positive.");

        if (AwaitingConfirmationMaxAge <= MinAwaitingConfirmationAge)
        {
            throw new InvalidOperationException(
                $"{SectionName}:AwaitingConfirmationMaxAge must be greater than MinAwaitingConfirmationAge.");
        }

        if (AwaitingConfirmationMaxAge >= TimeSpan.FromHours(12))
        {
            throw new InvalidOperationException(
                $"{SectionName}:AwaitingConfirmationMaxAge must stay below Kavenegar's 12-hour local-id lookup window.");
        }

        if (RequiredNegativeConfirmations < 2)
            throw new InvalidOperationException($"{SectionName}:RequiredNegativeConfirmations must be at least 2.");

        if (RetryBackoffSeconds.Any(seconds => seconds < 0))
            throw new InvalidOperationException($"{SectionName}:RetryBackoffSeconds cannot contain negative values.");
    }

    public TimeSpan RetryDelayForAttempt(int dispatchAttemptCount)
    {
        if (RetryBackoffSeconds.Length == 0)
            return TimeSpan.Zero;

        int index = Math.Clamp(dispatchAttemptCount - 1, 0, RetryBackoffSeconds.Length - 1);
        return TimeSpan.FromSeconds(RetryBackoffSeconds[index]);
    }
}
