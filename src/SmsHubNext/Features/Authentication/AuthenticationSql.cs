namespace SmsHubNext.Features.Authentication;

internal static class AuthenticationSql
{
    // Seek the key by its SHA-256 hash (UX_ApiKey_KeyHash). Status is evaluated in C#
    // so each failure reason (inactive / revoked / expired) can be reported precisely.
    public const string ResolveByHash =
        """
        SELECT Id AS ApiKeyId, CustomerId, KeyPrefix, IsActive, ExpiresAtUtc, RevokedAtUtc
        FROM dbo.ApiKey
        WHERE KeyHash = @KeyHash;
        """;

    public const string ListRestrictions =
        "SELECT Cidr FROM dbo.ApiKeyIpRestriction WHERE ApiKeyId = @ApiKeyId;";
}
