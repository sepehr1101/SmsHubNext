namespace SmsHubNext.Features.ReferenceData;

internal static class SenderLinesSql
{
    public const string List =
        "SELECT Id, ProviderId, LineNumber, IsSharedLine, IsActive FROM dbo.SenderLine ORDER BY Id;";
}
