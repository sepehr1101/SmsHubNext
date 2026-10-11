namespace SmsHubNext.App.Features.Tariffs;

public enum SmsEncoding : byte { Gsm7 = 0, Ucs2 = 1 }
public sealed record TariffRateItem(int Id, int TariffId, short MinChars, short? MaxChars, decimal PricePerSegment);
public sealed record TariffItem(int Id, byte ProviderId, byte? MessageTypeId, SmsEncoding Encoding, DateTime EffectiveFromUtc, DateTime? EffectiveToUtc, string Currency, bool IsActive, IReadOnlyList<TariffRateItem> Rates);
public sealed record CreateTariffRateRequest(short MinChars, short? MaxChars, decimal PricePerSegment);
public sealed record CreateTariffRequest(byte ProviderId, byte? MessageTypeId, SmsEncoding Encoding, DateTime EffectiveFromUtc, DateTime? EffectiveToUtc, bool IsActive, IReadOnlyList<CreateTariffRateRequest> Rates);
public sealed record CreateTariffResponse(int Id);
public sealed record UpdateTariffRequest(DateTime? EffectiveToUtc, bool IsActive);
