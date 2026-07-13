namespace SmsHubNext.Features.ReferenceData.MessageTypes;

internal static class MessageTypesSql
{
    public const string List =
        "SELECT Id, Name, Code, IsActive FROM dbo.MessageType WHERE DeletedAtUtc IS NULL ORDER BY Id;";

    // Id is caller-supplied: the MessageType key is a stable TINYINT, not an identity (README §4.6).
    public const string Insert =
        """
        INSERT INTO dbo.MessageType (Id, Name, Code)
        VALUES (@Id, @Name, @Code);
        """;

    public const string Update =
        """
        UPDATE dbo.MessageType
        SET Name = @Name, IsActive = @IsActive
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;

    public const string SoftDelete =
        """
        UPDATE dbo.MessageType
        SET IsActive = 0,
            DeletedAtUtc = SYSUTCDATETIME(),
            DeletedByApiKeyId = @DeletedByApiKeyId
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;
}
