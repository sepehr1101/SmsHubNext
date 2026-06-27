using SmsHubNext.Shared.Sms;

namespace SmsHubNext.Features.Tariffs;

/// <summary>A versioned tariff header (README §4.8).</summary>
public sealed record Tariff(
    int Id,
    byte ProviderId,
    byte? MessageTypeId,
    SmsEncoding Encoding,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    string Currency,
    bool IsActive);
