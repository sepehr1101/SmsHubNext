namespace SmsHubNext.Features.ReferenceData;

internal static class ProvidersSql
{
    public const string List = "SELECT Id, Name, Code, IsActive FROM dbo.Provider ORDER BY Id;";
}
