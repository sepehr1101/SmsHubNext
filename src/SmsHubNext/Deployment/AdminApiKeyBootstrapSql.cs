namespace SmsHubNext.Deployment;

internal static class AdminApiKeyBootstrapSql
{
    public const string Insert =
        """
        DECLARE @CustomerId SMALLINT;

        SELECT @CustomerId = Id
        FROM dbo.Customer WITH (UPDLOCK, HOLDLOCK)
        WHERE IsSystem = 1;

        IF @CustomerId IS NULL
        BEGIN
            INSERT INTO dbo.Customer (Name, Code, IsActive, IsSystem)
            VALUES (N'SmsHubNext Administration', 'smshubnext-administration', 1, 1);

            SET @CustomerId = CONVERT(SMALLINT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            UPDATE dbo.Customer
            SET IsActive = 1,
                DeletedAtUtc = NULL,
                DeletedByApiKeyId = NULL
            WHERE Id = @CustomerId;
        END;

        INSERT INTO dbo.ApiKey
            (CustomerId, Name, KeyPrefix, KeyHash, AccessLevel, ExpiresAtUtc)
        OUTPUT INSERTED.Id
        VALUES
            (@CustomerId, @Name, @KeyPrefix, @KeyHash, @AccessLevel, @ExpiresAtUtc);
        """;
}
