using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// Maps Magfa DLR codes (API reference §7 — a code space distinct from the §8 send-error codes)
/// onto our normalized <see cref="DeliveryReportStatus"/>. A <c>null</c> result means the message
/// is still in flight (no terminal outcome yet) and the poller should keep polling it.
/// </summary>
public static class MagfaDeliveryStatusCodes
{
    public static DeliveryReportStatus? Classify(int magfaDlrStatus) => magfaDlrStatus switch
    {
        1 => DeliveryReportStatus.Delivered,    // delivered to handset (terminal)
        2 => DeliveryReportStatus.Undelivered,  // not delivered to handset (terminal)
        16 => DeliveryReportStatus.Undelivered, // not delivered to operator (terminal)
        -1 => DeliveryReportStatus.Expired,     // id gone: wrong id, or >24h since send (terminal)
        8 => null,                              // delivered to operator, not yet handset (in-flight)
        0 => null,                              // no status received yet (in-flight)
        _ => null,                              // unknown code: keep polling (the status window bounds it)
    };
}
