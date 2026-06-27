using SmsHubNext.Shared.Sms;

namespace SmsHubNext.Features.Tariffs;

/// <summary>A tariff together with its price bands.</summary>
public sealed record TariffResponse(
    int Id,
    byte ProviderId,
    byte? MessageTypeId,
    SmsEncoding Encoding,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    string Currency,
    bool IsActive,
    IReadOnlyList<TariffRate> Rates);
