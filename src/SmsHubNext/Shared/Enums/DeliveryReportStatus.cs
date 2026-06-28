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

public static class DeliveryReportStatusExtensions
{
    /// <summary>
    /// Project a normalized report status onto the <see cref="DeliveryStatus"/> read model
    /// (README §4.10/§4.12). A <see cref="DeliveryReportStatus.Rejected"/> network report means
    /// the message was not delivered; the read model has no Rejected state of its own (send-side
    /// rejection lives on <c>Message.Status</c>).
    /// </summary>
    public static DeliveryStatus ToDeliveryStatus(this DeliveryReportStatus status) => status switch
    {
        DeliveryReportStatus.Delivered => DeliveryStatus.Delivered,
        DeliveryReportStatus.Undelivered => DeliveryStatus.Undelivered,
        DeliveryReportStatus.Expired => DeliveryStatus.Expired,
        DeliveryReportStatus.Rejected => DeliveryStatus.Undelivered,
        _ => DeliveryStatus.Unknown,
    };
}
