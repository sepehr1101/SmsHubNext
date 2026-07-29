using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.Providers.SmsIr;

public static class SmsIrStatusCodes
{
    public const int Success = 1;
    public const int SystemError = 0;
    public const int InvalidApiKey = 10;
    public const int InactiveApiKey = 11;
    public const int IpRestricted = 12;
    public const int InactiveAccount = 13;
    public const int SuspendedAccount = 14;
    public const int RateLimited = 20;
    public const int InvalidLine = 101;
    public const int InsufficientCredit = 102;
    public const int NotFound = 111;
    public const int Blacklisted = 115;

    public static bool IsDefinitelyNotSubmitted(int status) => status is
        InvalidApiKey or InactiveApiKey or IpRestricted or InactiveAccount or SuspendedAccount;

    public static bool IsRetryableNotSubmitted(int status) => status is SystemError or RateLimited;

    public static bool IsRejectedRequest(int status) => status is
        101 or 103 or 104 or 105 or 106 or 107 or 108 or 109 or 110 or
        112 or 113 or 114 or 115 or 116 or 117 or 118 or 119 or 123;

    public static DeliveryReportStatus? ClassifyDelivery(int? status) => status switch
    {
        1 => DeliveryReportStatus.Delivered,
        2 or 4 or 6 or 7 => DeliveryReportStatus.Undelivered,
        3 or 5 or null => null,
        _ => null,
    };
}
