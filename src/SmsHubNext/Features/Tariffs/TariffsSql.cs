namespace SmsHubNext.Features.Tariffs;

internal static class TariffsSql
{
    public const string ListTariffs =
        """
        SELECT Id, ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, EffectiveToUtc, Currency, IsActive
        FROM dbo.Tariff
        ORDER BY Id;
        """;

    public const string ListRates =
        """
        SELECT Id, TariffId, MinChars, MaxChars, PricePerSegment
        FROM dbo.TariffRate
        ORDER BY TariffId, MinChars;
        """;
}
