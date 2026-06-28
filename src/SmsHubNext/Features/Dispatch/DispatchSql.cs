namespace SmsHubNext.Features.Dispatch;

internal static class DispatchSql
{
    // Atomically claim the oldest dispatchable batch and move it to Dispatching.
    // Claimable = Received, Held long enough ago to retry (resume), or stuck in Dispatching
    // past the lease (a crashed/recycled worker — reprocessing only the still-Queued rows is
    // safe). READPAST/UPDLOCK let multiple workers skip each other's in-flight rows.
    // DispatchStartedAtUtc is stamped once (the first attempt), so queue-wait stays meaningful.
    public const string ClaimNextBatch =
        """
        ;WITH next AS (
            SELECT TOP (1) Id
            FROM dbo.MessageBatch WITH (READPAST, UPDLOCK, ROWLOCK)
            WHERE Status = 1                                                  -- Received
               OR (Status = 5 AND StatusChangedAtUtc <= @HeldRetryBefore)    -- Held, due for resume
               OR (Status = 2 AND StatusChangedAtUtc <= @DispatchStaleBefore) -- Dispatching, lease expired
            ORDER BY Id
        )
        UPDATE b
        SET Status = 2,                                        -- Dispatching
            StatusReason = NULL,
            DispatchStartedAtUtc = COALESCE(b.DispatchStartedAtUtc, @Now),
            StatusChangedAtUtc = @Now
        OUTPUT INSERTED.Id, INSERTED.CustomerId, INSERTED.ProviderId, INSERTED.SenderLineId
        FROM dbo.MessageBatch b
        INNER JOIN next ON next.Id = b.Id;
        """;

    public const string GetSenderLineNumber =
        "SELECT LineNumber FROM dbo.SenderLine WHERE Id = @SenderLineId;";

    // Only the still-Queued messages — so a resumed batch reprocesses just the unsent rows (idempotent).
    public const string LoadQueuedMessages =
        """
        SELECT m.Id, m.MobileNumber, m.TotalCost, m.CustomerId, body.Body
        FROM dbo.Message m
        INNER JOIN dbo.MessageBody body ON body.Id = m.Id
        WHERE m.MessageBatchId = @BatchId AND m.Status = 1     -- Queued
        ORDER BY m.Id;
        """;

    // Guarded transitions (WHERE Status = 1) make every update idempotent under retry/restart.
    public const string MarkSubmitted =
        """
        UPDATE dbo.Message
        SET Status = 2, ProviderMessageId = @ProviderMessageId   -- Submitted
        WHERE Id = @Id AND Status = 1;
        """;

    public const string MarkRejected =
        """
        UPDATE dbo.Message
        SET Status = 4, ProviderMessageId = @ProviderMessageId   -- Rejected
        WHERE Id = @Id AND Status = 1;
        """;

    // Refund a provider-rejected message: credit the balance and append the ledger entry.
    public const string CreditBalance =
        """
        UPDATE dbo.CustomerBalance
        SET Balance += @Amount, UpdatedAtUtc = @Now
        OUTPUT INSERTED.Balance
        WHERE CustomerId = @CustomerId;
        """;

    public const string InsertRefundLedger =
        """
        INSERT INTO dbo.BalanceTransaction (CustomerId, Type, Amount, BalanceAfter, MessageBatchId, Reference)
        VALUES (@CustomerId, @Type, @Amount, @BalanceAfter, @MessageBatchId, @Reference);
        """;

    public const string HoldBatch =
        """
        UPDATE dbo.MessageBatch
        SET Status = 5, StatusReason = @Reason, StatusChangedAtUtc = @Now   -- Held
        WHERE Id = @Id;
        """;

    // Re-queue a batch after a transient error so it is reclaimed on a later cycle.
    public const string RevertBatchToReceived =
        """
        UPDATE dbo.MessageBatch
        SET Status = 1, StatusChangedAtUtc = @Now                          -- Received
        WHERE Id = @Id;
        """;

    public const string FinalizeBatch =
        """
        UPDATE dbo.MessageBatch
        SET Status = @Status, StatusReason = NULL, FinishedAtUtc = @Now, StatusChangedAtUtc = @Now
        WHERE Id = @Id;
        """;

    // The batch's message-status distribution, used to pick the terminal batch status.
    public const string CountMessageStatuses =
        """
        SELECT
            SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END) AS Queued,
            SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) AS Submitted,
            SUM(CASE WHEN Status = 4 THEN 1 ELSE 0 END) AS Rejected
        FROM dbo.Message
        WHERE MessageBatchId = @BatchId;
        """;
}
