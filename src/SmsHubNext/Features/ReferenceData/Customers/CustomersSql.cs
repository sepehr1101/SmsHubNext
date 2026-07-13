namespace SmsHubNext.Features.ReferenceData.Customers;

internal static class CustomersSql
{
    public const string List =
        "SELECT Id, Name, Code, IsActive, CreatedAtUtc FROM dbo.Customer WHERE DeletedAtUtc IS NULL ORDER BY Id;";

    public const string Insert =
        "INSERT INTO dbo.Customer (Name, Code) OUTPUT INSERTED.Id VALUES (@Name, @Code);";

    public const string Update =
        """
        UPDATE dbo.Customer
        SET Name = @Name, Code = @Code, IsActive = @IsActive
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;

    public const string SoftDelete =
        """
        UPDATE dbo.Customer
        SET IsActive = 0,
            DeletedAtUtc = SYSUTCDATETIME(),
            DeletedByApiKeyId = @DeletedByApiKeyId
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;
}
