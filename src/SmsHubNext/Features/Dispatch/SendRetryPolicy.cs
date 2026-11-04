namespace SmsHubNext.Features.Dispatch;

public static class SendRetryPolicy
{
    // One initial submission plus at most three retries. Persisted before transport calls;
    // restarts and manual batch retries cannot reset this budget.
    public const int MaximumAttempts = 4;

    public static bool MustHold(int attempts, DateTime? firstAttemptAtUtc, DateTime now, TimeSpan? window) =>
        attempts >= MaximumAttempts ||
        (window is TimeSpan duration && firstAttemptAtUtc is DateTime first && now >= first + duration);
}
