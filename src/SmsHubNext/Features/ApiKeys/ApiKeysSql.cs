namespace SmsHubNext.Features.ApiKeys;

internal static class ApiKeysSql
{
    public const string Insert =
        """
        INSERT INTO dbo.ApiKey (CustomerId, Name, KeyPrefix, KeyHash, ExpiresAtUtc)
        OUTPUT INSERTED.Id
        VALUES (@CustomerId, @Name, @KeyPrefix, @KeyHash, @ExpiresAtUtc);
        """;

    public const string ListByCustomer =
        """
        SELECT Id, CustomerId, Name, KeyPrefix, IsActive, ExpiresAtUtc, RevokedAtUtc, CreatedAtUtc
        FROM dbo.ApiKey
        WHERE CustomerId = @CustomerId
        ORDER BY Id;
        """;
}
