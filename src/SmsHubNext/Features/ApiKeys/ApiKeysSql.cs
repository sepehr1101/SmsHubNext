namespace SmsHubNext.Features.ApiKeys;

internal static class ApiKeysSql
{
    public const string Insert =
        """
        INSERT INTO dbo.ApiKey (CustomerId, Name, KeyPrefix, KeyHash, ExpiresAtUtc)
        OUTPUT INSERTED.Id
        SELECT @CustomerId, @Name, @KeyPrefix, @KeyHash, @ExpiresAtUtc
        FROM dbo.Customer
        WHERE Id = @CustomerId AND DeletedAtUtc IS NULL;
        """;

    public const string ListByCustomer =
        """
        SELECT Id, CustomerId, Name, KeyPrefix, IsActive, ExpiresAtUtc, RevokedAtUtc,
               RevokedByApiKeyId, CreatedAtUtc
        FROM dbo.ApiKey
        WHERE CustomerId = @CustomerId
        ORDER BY Id;
        """;

    public const string GetState =
        "SELECT RevokedAtUtc FROM dbo.ApiKey WHERE Id = @Id;";

    public const string Update =
        """
        UPDATE dbo.ApiKey
        SET Name = @Name, ExpiresAtUtc = @ExpiresAtUtc, IsActive = @IsActive
        WHERE Id = @Id AND RevokedAtUtc IS NULL;
        """;

    public const string Revoke =
        """
        UPDATE dbo.ApiKey
        SET IsActive = 0,
            RevokedAtUtc = SYSUTCDATETIME(),
            RevokedByApiKeyId = @RevokedByApiKeyId
        WHERE Id = @Id AND RevokedAtUtc IS NULL;
        """;

    public const string Exists =
        "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ApiKey WHERE Id = @Id) THEN 1 ELSE 0 END AS bit);";
}
