using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.Providers.Kavenegar;

public static class KavenegarStatusCodes
{
    public const int Success = 200;
    public const int TemporaryUnavailable = 409;
    public const int InsufficientCredit = 418;
    public const int InvalidMessageId = 100;

    /// <summary>Inactive-account, invalid-API-key, and access/IP request failures.</summary>
    public static bool IsDefinitelyNotSubmitted(int status) => status is 401 or 403 or 407;

    public static bool IsAcceptedSendStatus(int status) => status is 1 or 2 or 4 or 5 or 10;

    public static bool IsRejectedSendStatus(int status) => status is 6 or 11 or 13 or 14 or 100;

    public static DeliveryReportStatus? ClassifyDelivery(int status) => status switch
    {
        10 => DeliveryReportStatus.Delivered,
        6 or 11 or 13 or 14 => DeliveryReportStatus.Undelivered,
        100 => DeliveryReportStatus.Expired,
        1 or 2 or 4 or 5 => null,
        _ => null,
    };
}
