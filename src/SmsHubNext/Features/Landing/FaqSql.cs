namespace SmsHubNext.Features.Landing;

internal static class FaqSql
{
    internal const string List = "SELECT Id, Question, Answer, SortOrder FROM dbo.FrequentlyAskedQuestion ORDER BY SortOrder, Id;";
    internal const string LockedList = "SELECT Id, Question, Answer, SortOrder FROM dbo.FrequentlyAskedQuestion WITH (UPDLOCK, HOLDLOCK) ORDER BY SortOrder, Id;";
    internal const string Insert = """
        INSERT dbo.FrequentlyAskedQuestion (Question, Answer, SortOrder, UpdatedByApiKeyId)
        SELECT @Question, @Answer, COALESCE(MAX(SortOrder), 0) + 1, @ActorId
        FROM dbo.FrequentlyAskedQuestion WITH (UPDLOCK, HOLDLOCK);
        """;
    internal const string Update = "UPDATE dbo.FrequentlyAskedQuestion SET Question = @Question, Answer = @Answer, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedByApiKeyId = @ActorId WHERE Id = @Id;";
    internal const string Delete = "DELETE dbo.FrequentlyAskedQuestion WHERE Id = @Id;";
    internal const string Reorder = "UPDATE dbo.FrequentlyAskedQuestion SET SortOrder = @SortOrder, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedByApiKeyId = @ActorId WHERE Id = @Id;";
}
