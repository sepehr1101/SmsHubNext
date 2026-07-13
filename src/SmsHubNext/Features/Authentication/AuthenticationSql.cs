namespace SmsHubNext.Features.Authentication;

internal static class AuthenticationSql
{
    // Seek the key by its SHA-256 hash (UX_ApiKey_KeyHash). Status is evaluated in C#
    // so each failure reason (inactive / revoked / expired) can be reported precisely.
    public const string ResolveByHash =
        """
        SELECT ak.Id AS ApiKeyId, ak.CustomerId, ak.KeyPrefix, ak.IsActive, ak.ExpiresAtUtc, ak.RevokedAtUtc
        FROM dbo.ApiKey ak
        INNER JOIN dbo.Customer c ON c.Id = ak.CustomerId
        WHERE ak.KeyHash = @KeyHash
          AND c.IsActive = 1
          AND c.DeletedAtUtc IS NULL;
        """;

    public const string ListRestrictions =
        "SELECT Cidr FROM dbo.ApiKeyIpRestriction WHERE ApiKeyId = @ApiKeyId AND DeletedAtUtc IS NULL;";
}
