namespace SmsHubNext.Features.ReferenceData.Providers;

internal static class ProvidersSql
{
    public const string List =
        "SELECT Id, Name, Code, IsActive FROM dbo.Provider WHERE DeletedAtUtc IS NULL ORDER BY Id;";

    public const string Insert =
        """
        INSERT INTO dbo.Provider (Name, Code, BaseUrl, FallbackBaseUrl)
        OUTPUT INSERTED.Id
        VALUES (@Name, @Code, @BaseUrl, @FallbackBaseUrl);
        """;

    public const string Update =
        """
        UPDATE dbo.Provider
        SET Name = @Name,
            BaseUrl = @BaseUrl,
            FallbackBaseUrl = @FallbackBaseUrl,
            IsActive = @IsActive
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;

    public const string SoftDelete =
        """
        UPDATE dbo.Provider
        SET IsActive = 0,
            DeletedAtUtc = SYSUTCDATETIME(),
            DeletedByApiKeyId = @DeletedByApiKeyId
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;
}
