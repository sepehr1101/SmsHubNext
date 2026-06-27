using SmsHubNext.Shared.Sms;

namespace SmsHubNext.Features.Tariffs;

/// <summary>
/// The resolved price for a message — exactly the values frozen onto the
/// <c>Message</c> at submission (README §6.3).
/// </summary>
public sealed record CostQuote(
    SmsEncoding Encoding,
    int CharacterCount,
    int SegmentCount,
    int TariffId,
    decimal UnitPrice,
    decimal TotalCost,
    string Currency);
