namespace SmsHubNext.Features.ReferenceData.SenderLines;

internal static class SenderLinesSql
{
    public const string List =
        "SELECT Id, ProviderId, LineNumber, IsSharedLine, CustomerId, ProviderAccountId, IsActive FROM dbo.SenderLine ORDER BY Id;";

    public const string Insert =
        """
        INSERT INTO dbo.SenderLine (ProviderId, LineNumber, IsSharedLine, CustomerId, ProviderAccountId, IsActive)
        OUTPUT INSERTED.Id
        VALUES (@ProviderId, @LineNumber, @IsSharedLine, @CustomerId, @ProviderAccountId, @IsActive);
        """;

    public const string GetBinding =
        """
        SELECT Id, ProviderId
        FROM dbo.SenderLine
        WHERE Id = @Id;
        """;

    public const string GetProviderAccountBinding =
        """
        SELECT ProviderId, IsActive
        FROM dbo.ProviderAccount
        WHERE Id = @ProviderAccountId;
        """;

    public const string AssignProviderAccount =
        """
        UPDATE dbo.SenderLine
        SET ProviderAccountId = @ProviderAccountId
        WHERE Id = @Id;
        """;
}
