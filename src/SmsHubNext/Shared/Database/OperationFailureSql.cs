namespace SmsHubNext.Shared.Database;

internal static class OperationFailureSql
{
    // One event per affected batch, not one write per recipient. Uses existing event retention.
    public const string Insert = """
        INSERT INTO dbo.MessageBatchEvent (MessageBatchId, EventTimeUtc, EventType, Detail, FailureJson)
        SELECT DISTINCT MessageBatchId, @Now, @EventType, @Detail, @FailureJson
        FROM dbo.Message WHERE Id IN (SELECT Id FROM OPENJSON(@MessageIdsJson) WITH (Id BIGINT '$'));
        """;
}
