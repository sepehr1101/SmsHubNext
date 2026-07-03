namespace SmsHubNext.Features.Sending;

internal static class SendingSql
{
    // Resolve the requested sender line to its keys; only active lines may send.
    public const string ResolveSenderLine =
        """
        SELECT Id, ProviderId, IsActive
        FROM dbo.SenderLine
        WHERE LineNumber = @LineNumber;
        """;

    public const string CustomerExists =
        """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.Customer
            WHERE Id = @CustomerId AND IsActive = 1
        ) THEN 1 ELSE 0 END AS bit);
        """;

    public const string ApiKeyBelongsToCustomer =
        """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.ApiKey
            WHERE Id = @ApiKeyId AND CustomerId = @CustomerId
        ) THEN 1 ELSE 0 END AS bit);
        """;

    public const string MessageTypeExists =
        """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.MessageType
            WHERE Id = @MessageTypeId
        ) THEN 1 ELSE 0 END AS bit);
        """;

    public const string CountExistingGeoSections =
        """
        SELECT COUNT_BIG(*)
        FROM dbo.GeoSection
        WHERE Id IN @GeoSectionIds AND IsActive = 1;
        """;

    // Overspend-safe debit: a single atomic statement. OUTPUT returns the post-debit
    // balance, or no rows when funds are insufficient or the customer has no balance row
    // (README §4.14/§8.6). No SELECT-then-UPDATE race.
    public const string DebitBalance =
        """
        UPDATE dbo.CustomerBalance
        SET Balance -= @Amount, UpdatedAtUtc = SYSUTCDATETIME()
        OUTPUT INSERTED.Balance
        WHERE CustomerId = @CustomerId AND Balance >= @Amount;
        """;

    public const string InsertBatch =
        """
        INSERT INTO dbo.MessageBatch
            (SubmitDateJalali, ReceivedAtUtc, CustomerId, ApiKeyId, SenderLineId, ProviderId,
             ClientBatchId, MessageCount, SegmentCount, TotalCost, Status, StatusChangedAtUtc)
        OUTPUT INSERTED.Id
        VALUES
            (@SubmitDateJalali, @NowUtc, @CustomerId, @ApiKeyId, @SenderLineId, @ProviderId,
             @ClientBatchId, @MessageCount, @SegmentCount, @TotalCost, @Status, @NowUtc);
        """;

    public const string InsertDebitLedger =
        """
        INSERT INTO dbo.BalanceTransaction
            (CustomerId, Type, Amount, BalanceAfter, MessageBatchId, Reference)
        VALUES
            (@CustomerId, @Type, @Amount, @BalanceAfter, @MessageBatchId, @Reference);
        """;

    public const string InsertBatchEvent =
        """
        INSERT INTO dbo.MessageBatchEvent
            (MessageBatchId, EventTimeUtc, EventType, BatchStatus, Detail)
        VALUES
            (@MessageBatchId, @NowUtc, @EventType, @BatchStatus, @Detail);
        """;

    // The Message and MessageBody rows are bulk-inserted (SqlBulkCopy) rather than looped, so the
    // column lists live in SendMessagesHandler's DataTable builders. After the message bulk insert
    // we read the server-assigned ids back in insertion order to key the 1:1 bodies: all rows share
    // this batch's fresh MessageBatchId, and a single bulk-copy stream assigns identity in row order,
    // so ORDER BY Id reproduces that order.
    public const string SelectBatchMessageIds =
        "SELECT Id FROM dbo.Message WHERE MessageBatchId = @MessageBatchId ORDER BY Id;";
}
