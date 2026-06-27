namespace SmsHubNext.Shared.Enums;

/// <summary>
/// Current delivery state of a <c>Message</c> — the denormalized read model that
/// makes success rate a join-free <c>GROUP BY</c> (README §4.10).
/// Persisted as <c>TINYINT</c> — values are stable and must not be renumbered.
/// </summary>
public enum DeliveryStatus : byte
{
    Pending = 1,
    Delivered = 2,
    Undelivered = 3,
    Expired = 4,
    Unknown = 5,
}

public static class DeliveryStatusExtensions
{
    /// <summary>
    /// True once a final outcome is known. Only <see cref="DeliveryStatus.Pending"/>
    /// is non-terminal; afterwards the read model stops changing, so the in-place
    /// update only ever lands on the hot rowstore partition (README §4.10).
    /// </summary>
    public static bool IsTerminal(this DeliveryStatus status) => status != DeliveryStatus.Pending;
}
