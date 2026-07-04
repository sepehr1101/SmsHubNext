namespace SmsHubNext.Features.ReferenceData;

internal static class SenderLinesSql
{
    public const string List =
        "SELECT Id, ProviderId, LineNumber, IsSharedLine, CustomerId, IsActive FROM dbo.SenderLine ORDER BY Id;";

    public const string Insert =
        """
        INSERT INTO dbo.SenderLine (ProviderId, LineNumber, IsSharedLine, CustomerId, IsActive)
        OUTPUT INSERTED.Id
        VALUES (@ProviderId, @LineNumber, @IsSharedLine, @CustomerId, @IsActive);
        """;
}
