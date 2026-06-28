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

    public const string InsertMessage =
        """
        INSERT INTO dbo.Message
            (SubmitDateJalali, MessageBatchId, SubmittedAtUtc, CustomerId, ProviderId, SenderLineId,
             MessageTypeId, GeoSectionId, MobileNumber, ClientCorrelatedId, BillId, PayId,
             Encoding, CharacterCount, SegmentCount, TariffId, UnitPrice, TotalCost, Status, DeliveryStatus)
        OUTPUT INSERTED.Id
        VALUES
            (@SubmitDateJalali, @MessageBatchId, @NowUtc, @CustomerId, @ProviderId, @SenderLineId,
             @MessageTypeId, @GeoSectionId, @MobileNumber, @ClientCorrelatedId, @BillId, @PayId,
             @Encoding, @CharacterCount, @SegmentCount, @TariffId, @UnitPrice, @TotalCost, @Status, @DeliveryStatus);
        """;

    public const string InsertBody =
        "INSERT INTO dbo.MessageBody (Id, Body) VALUES (@Id, @Body);";
}
