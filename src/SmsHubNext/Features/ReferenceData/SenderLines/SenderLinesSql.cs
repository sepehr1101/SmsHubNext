namespace SmsHubNext.Features.ReferenceData.SenderLines;

internal static class SenderLinesSql
{
    public const string List =
        "SELECT Id, ProviderId, LineNumber, IsSharedLine, CustomerId, ProviderAccountId, IsActive FROM dbo.SenderLine WHERE DeletedAtUtc IS NULL ORDER BY Id;";

    public const string Insert =
        """
        INSERT INTO dbo.SenderLine (ProviderId, LineNumber, IsSharedLine, CustomerId, ProviderAccountId, IsActive)
        OUTPUT INSERTED.Id
        SELECT @ProviderId, @LineNumber, @IsSharedLine, @CustomerId, @ProviderAccountId, @IsActive
        FROM dbo.Provider p
        WHERE p.Id = @ProviderId
          AND p.DeletedAtUtc IS NULL
          AND (@CustomerId IS NULL OR EXISTS (
              SELECT 1 FROM dbo.Customer c WHERE c.Id = @CustomerId AND c.DeletedAtUtc IS NULL));
        """;

    public const string GetBinding =
        """
        SELECT Id, ProviderId
        FROM dbo.SenderLine
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;

    public const string GetProviderAccountBinding =
        """
        SELECT pa.ProviderId, pa.IsActive
        FROM dbo.ProviderAccount pa
        INNER JOIN dbo.Provider p ON p.Id = pa.ProviderId
        WHERE pa.Id = @ProviderAccountId
          AND pa.DeletedAtUtc IS NULL
          AND p.DeletedAtUtc IS NULL;
        """;

    public const string ProviderExists =
        "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Provider WHERE Id = @ProviderId AND DeletedAtUtc IS NULL) THEN 1 ELSE 0 END AS bit);";

    public const string CustomerExists =
        "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Customer WHERE Id = @CustomerId AND DeletedAtUtc IS NULL) THEN 1 ELSE 0 END AS bit);";

    public const string AssignProviderAccount =
        """
        UPDATE dbo.SenderLine
        SET ProviderAccountId = @ProviderAccountId
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;

    public const string Update =
        """
        UPDATE dbo.SenderLine
        SET LineNumber = @LineNumber,
            IsSharedLine = @IsSharedLine,
            CustomerId = @CustomerId,
            ProviderAccountId = @ProviderAccountId,
            IsActive = @IsActive
        WHERE Id = @Id
          AND DeletedAtUtc IS NULL
          AND (@CustomerId IS NULL OR EXISTS (
              SELECT 1 FROM dbo.Customer c WHERE c.Id = @CustomerId AND c.DeletedAtUtc IS NULL));
        """;

    public const string SoftDelete =
        """
        UPDATE dbo.SenderLine
        SET IsActive = 0,
            DeletedAtUtc = SYSUTCDATETIME(),
            DeletedByApiKeyId = @DeletedByApiKeyId
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;
}
