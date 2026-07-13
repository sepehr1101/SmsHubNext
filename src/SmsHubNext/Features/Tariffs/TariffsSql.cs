namespace SmsHubNext.Features.Tariffs;

internal static class TariffsSql
{
    public const string ListTariffs =
        """
        SELECT Id, ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, EffectiveToUtc, Currency, IsActive
        FROM dbo.Tariff
        WHERE DeletedAtUtc IS NULL
        ORDER BY Id;
        """;

    public const string ListRates =
        """
        SELECT r.Id, r.TariffId, r.MinChars, r.MaxChars, r.PricePerSegment
        FROM dbo.TariffRate r
        INNER JOIN dbo.Tariff t ON t.Id = r.TariffId
        WHERE t.DeletedAtUtc IS NULL
        ORDER BY TariffId, MinChars;
        """;

    // Resolve the applicable tariff + price band: most specific message type wins,
    // then most recent effective date. Returns no row when nothing matches.
    public const string ResolveRate =
        """
        SELECT TOP (1) t.Id AS TariffId, t.Currency, r.PricePerSegment
        FROM dbo.Tariff t
        INNER JOIN dbo.TariffRate r ON r.TariffId = t.Id
        INNER JOIN dbo.Provider p ON p.Id = t.ProviderId
        LEFT JOIN dbo.MessageType mt ON mt.Id = t.MessageTypeId
        WHERE t.ProviderId = @ProviderId
          AND (t.MessageTypeId = @MessageTypeId OR t.MessageTypeId IS NULL)
          AND t.Encoding = @Encoding
          AND t.IsActive = 1
          AND t.DeletedAtUtc IS NULL
          AND p.IsActive = 1
          AND p.DeletedAtUtc IS NULL
          AND (t.MessageTypeId IS NULL OR (mt.IsActive = 1 AND mt.DeletedAtUtc IS NULL))
          AND t.EffectiveFromUtc <= SYSUTCDATETIME()
          AND (t.EffectiveToUtc IS NULL OR t.EffectiveToUtc > SYSUTCDATETIME())
          AND r.MinChars <= @CharacterCount
          AND (r.MaxChars IS NULL OR r.MaxChars >= @CharacterCount)
        ORDER BY CASE WHEN t.MessageTypeId = @MessageTypeId THEN 0 ELSE 1 END, t.EffectiveFromUtc DESC;
        """;

    public const string ValidateReferences =
        """
        SELECT
            CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Provider WITH (UPDLOCK, HOLDLOCK)
                WHERE Id = @ProviderId AND DeletedAtUtc IS NULL
            ) THEN 1 ELSE 0 END AS bit) AS ProviderExists,
            CAST(CASE WHEN @MessageTypeId IS NULL OR EXISTS (
                SELECT 1 FROM dbo.MessageType WITH (UPDLOCK, HOLDLOCK)
                WHERE Id = @MessageTypeId AND DeletedAtUtc IS NULL
            ) THEN 1 ELSE 0 END AS bit) AS MessageTypeExists;
        """;

    public const string HasOverlappingTariff =
        """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.Tariff WITH (UPDLOCK, HOLDLOCK)
            WHERE ProviderId = @ProviderId
              AND ((MessageTypeId = @MessageTypeId) OR (MessageTypeId IS NULL AND @MessageTypeId IS NULL))
              AND Encoding = @Encoding
              AND IsActive = 1
              AND DeletedAtUtc IS NULL
              AND (@ExcludeId IS NULL OR Id <> @ExcludeId)
              AND EffectiveFromUtc < COALESCE(@EffectiveToUtc, CONVERT(DATETIME2(3), '9999-12-31 23:59:59.997'))
              AND COALESCE(EffectiveToUtc, CONVERT(DATETIME2(3), '9999-12-31 23:59:59.997')) > @EffectiveFromUtc
        ) THEN 1 ELSE 0 END AS bit);
        """;

    public const string InsertTariff =
        """
        INSERT INTO dbo.Tariff
            (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, EffectiveToUtc, Currency, IsActive)
        OUTPUT INSERTED.Id
        VALUES
            (@ProviderId, @MessageTypeId, @Encoding, @EffectiveFromUtc, @EffectiveToUtc, 'IRR', @IsActive);
        """;

    public const string InsertRate =
        """
        INSERT INTO dbo.TariffRate (TariffId, MinChars, MaxChars, PricePerSegment)
        VALUES (@TariffId, @MinChars, @MaxChars, @PricePerSegment);
        """;

    public const string GetLifecycle =
        """
        SELECT Id, ProviderId, MessageTypeId, Encoding, EffectiveFromUtc
        FROM dbo.Tariff WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;

    public const string UpdateLifecycle =
        """
        UPDATE dbo.Tariff
        SET EffectiveToUtc = @EffectiveToUtc, IsActive = @IsActive
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;

    public const string SoftDelete =
        """
        UPDATE dbo.Tariff
        SET IsActive = 0,
            DeletedAtUtc = SYSUTCDATETIME(),
            DeletedByApiKeyId = @DeletedByApiKeyId
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;
}
