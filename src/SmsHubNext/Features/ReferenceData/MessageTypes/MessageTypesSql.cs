namespace SmsHubNext.Features.ReferenceData.MessageTypes;

internal static class MessageTypesSql
{
    public const string List = "SELECT Id, Name, Code FROM dbo.MessageType ORDER BY Id;";

    // Id is caller-supplied: the MessageType key is a stable TINYINT, not an identity (README §4.6).
    public const string Insert =
        """
        INSERT INTO dbo.MessageType (Id, Name, Code)
        VALUES (@Id, @Name, @Code);
        """;
}
