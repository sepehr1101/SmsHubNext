namespace SmsHubNext.Shared.Enums;

/// <summary>
/// Send-lifecycle of a single <c>Message</c> (the <c>Status</c> column, README §4.10).
/// Persisted as <c>TINYINT</c> — values are stable and must not be renumbered.
/// </summary>
public enum SendStatus : byte
{
    Queued = 1,
    Submitted = 2,
    Sent = 3,
    Rejected = 4,
    Unknown = 5,

    /// <summary>
    /// Handed to the provider, but the response was lost (transport failure), so the outcome is
    /// unknown. The dispatcher reconciles it on the next cycle — confirming it via the provider
    /// rather than blindly re-sending — so a lost response never double-charges (README §8.2).
    /// </summary>
    AwaitingConfirmation = 6,
}
