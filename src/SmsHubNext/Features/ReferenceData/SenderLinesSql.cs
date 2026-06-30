namespace SmsHubNext.Features.ReferenceData;

internal static class SenderLinesSql
{
    public const string List =
        "SELECT Id, ProviderId, LineNumber, IsSharedLine, IsActive FROM dbo.SenderLine ORDER BY Id;";

    public const string Insert =
        """
        INSERT INTO dbo.SenderLine (ProviderId, LineNumber, IsSharedLine, IsActive)
        OUTPUT INSERTED.Id
        VALUES (@ProviderId, @LineNumber, @IsSharedLine, @IsActive);
        """;
}
