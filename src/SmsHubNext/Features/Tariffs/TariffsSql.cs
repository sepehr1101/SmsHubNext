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

    // Resolve the applicable tariff + price band: most specific message type wins,
    // then most recent effective date. Returns no row when nothing matches.
    public const string ResolveRate =
        """
        SELECT TOP (1) t.Id AS TariffId, t.Currency, r.PricePerSegment
        FROM dbo.Tariff t
        INNER JOIN dbo.TariffRate r ON r.TariffId = t.Id
        WHERE t.ProviderId = @ProviderId
          AND (t.MessageTypeId = @MessageTypeId OR t.MessageTypeId IS NULL)
          AND t.Encoding = @Encoding
          AND t.IsActive = 1
          AND t.EffectiveFromUtc <= SYSUTCDATETIME()
          AND (t.EffectiveToUtc IS NULL OR t.EffectiveToUtc > SYSUTCDATETIME())
          AND r.MinChars <= @CharacterCount
          AND (r.MaxChars IS NULL OR r.MaxChars >= @CharacterCount)
        ORDER BY CASE WHEN t.MessageTypeId = @MessageTypeId THEN 0 ELSE 1 END, t.EffectiveFromUtc DESC;
        """;
}
