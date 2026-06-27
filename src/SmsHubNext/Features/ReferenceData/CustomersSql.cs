namespace SmsHubNext.Features.ReferenceData;

internal static class CustomersSql
{
    public const string List =
        "SELECT Id, Name, Code, IsActive, CreatedAtUtc FROM dbo.Customer ORDER BY Id;";

    public const string Insert =
        "INSERT INTO dbo.Customer (Name, Code) OUTPUT INSERTED.Id VALUES (@Name, @Code);";
}
