namespace SmsHubNext.Features.ApiKeys;

internal static class ApiKeyIpRestrictionsSql
{
    public const string Insert =
        """
        INSERT INTO dbo.ApiKeyIpRestriction (ApiKeyId, Cidr, Description)
        OUTPUT INSERTED.Id
        SELECT @ApiKeyId, @Cidr, @Description
        FROM dbo.ApiKey
        WHERE Id = @ApiKeyId AND IsActive = 1 AND RevokedAtUtc IS NULL;
        """;

    public const string ListByApiKey =
        """
        SELECT Id, ApiKeyId, Cidr, Description
        FROM dbo.ApiKeyIpRestriction
        WHERE ApiKeyId = @ApiKeyId AND DeletedAtUtc IS NULL
        ORDER BY Id;
        """;

    public const string Update =
        """
        UPDATE dbo.ApiKeyIpRestriction
        SET Cidr = @Cidr,
            Description = @Description,
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE Id = @Id AND ApiKeyId = @ApiKeyId AND DeletedAtUtc IS NULL;
        """;

    public const string SoftDelete =
        """
        UPDATE dbo.ApiKeyIpRestriction
        SET UpdatedAtUtc = SYSUTCDATETIME(),
            DeletedAtUtc = SYSUTCDATETIME(),
            DeletedByApiKeyId = @DeletedByApiKeyId
        WHERE Id = @Id AND ApiKeyId = @ApiKeyId AND DeletedAtUtc IS NULL;
        """;
}
