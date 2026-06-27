namespace SmsHubNext.Shared.Enums;

/// <summary>
/// Normalized status of a single <c>DeliveryReport</c> — a provider DLR mapped to
/// our vocabulary (README §4.12). Distinct from <see cref="DeliveryStatus"/>: a
/// report can be <c>Rejected</c>, which the message read model represents differently.
/// Persisted as <c>TINYINT</c> — values are stable and must not be renumbered.
/// </summary>
public enum DeliveryReportStatus : byte
{
    Delivered = 1,
    Undelivered = 2,
    Expired = 3,
    Rejected = 4,
    Unknown = 5,
}
