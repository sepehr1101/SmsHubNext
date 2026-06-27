namespace SmsHubNext.Features.Tariffs;

/// <summary>A per-segment price band within a tariff (README §4.9).</summary>
public sealed record TariffRate(int Id, int TariffId, short MinChars, short? MaxChars, decimal PricePerSegment);
