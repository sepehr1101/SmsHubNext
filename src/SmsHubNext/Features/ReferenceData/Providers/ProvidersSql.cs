namespace SmsHubNext.Features.ReferenceData.Providers;

internal static class ProvidersSql
{
    public const string List = "SELECT Id, Name, Code, IsActive FROM dbo.Provider ORDER BY Id;";

    public const string Insert =
        """
        INSERT INTO dbo.Provider (Name, Code, BaseUrl, FallbackBaseUrl)
        OUTPUT INSERTED.Id
        VALUES (@Name, @Code, @BaseUrl, @FallbackBaseUrl);
        """;
}
