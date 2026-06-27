namespace SmsHubNext.Features.ApiKeys;

internal static class ApiKeyIpRestrictionsSql
{
    public const string Insert =
        """
        INSERT INTO dbo.ApiKeyIpRestriction (ApiKeyId, Cidr, Description)
        OUTPUT INSERTED.Id
        VALUES (@ApiKeyId, @Cidr, @Description);
        """;

    public const string ListByApiKey =
        """
        SELECT Id, ApiKeyId, Cidr, Description
        FROM dbo.ApiKeyIpRestriction
        WHERE ApiKeyId = @ApiKeyId
        ORDER BY Id;
        """;
}
