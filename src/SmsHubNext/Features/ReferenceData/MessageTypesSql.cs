namespace SmsHubNext.Features.ReferenceData;

internal static class MessageTypesSql
{
    public const string List = "SELECT Id, Name, Code FROM dbo.MessageType ORDER BY Id;";
}
