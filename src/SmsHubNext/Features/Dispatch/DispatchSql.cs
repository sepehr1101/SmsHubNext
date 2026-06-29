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

    // Messages still needing dispatch: Queued (never sent) or AwaitingConfirmation (sent, response
    // lost — reconciled before re-sending). A resumed batch reprocesses just these rows (idempotent).
    public const string LoadDispatchableMessages =
        """
        SELECT m.Id, m.MobileNumber, m.TotalCost, m.CustomerId, body.Body, m.Status
        FROM dbo.Message m
        INNER JOIN dbo.MessageBody body ON body.Id = m.Id
        WHERE m.MessageBatchId = @BatchId AND m.Status IN (1, 6)   -- Queued, AwaitingConfirmation
        ORDER BY m.Id;
        """;

    // Guarded transition (Status Queued or AwaitingConfirmation -> Submitted) keeps every update
    // idempotent under retry/restart, and serves both a fresh send and a reconciled-confirmed send.
    // On the same transition, enqueue the message for delivery-report polling (Phase 2). The enqueue
    // is guarded (message now Submitted with this provider id, and not already queued), so a retried
    // submit never double-enqueues. DispatchedAtUtc/@Now anchors the status window.
    public const string MarkSubmitted =
        """
        UPDATE dbo.Message
        SET Status = 2, ProviderMessageId = @ProviderMessageId   -- Submitted
        WHERE Id = @Id AND Status IN (1, 6);                     -- Queued or AwaitingConfirmation

        INSERT INTO dbo.DeliveryReportPoll
            (MessageId, SubmitDateJalali, ProviderId, ProviderMessageId, DispatchedAtUtc, NextPollAtUtc, Attempts)
        SELECT m.Id, m.SubmitDateJalali, m.ProviderId, m.ProviderMessageId, @Now, @Now, 0
        FROM dbo.Message m
        WHERE m.Id = @Id AND m.Status = 2 AND m.ProviderMessageId = @ProviderMessageId
          AND NOT EXISTS (SELECT 1 FROM dbo.DeliveryReportPoll p WHERE p.MessageId = m.Id);
        """;

    // Park messages whose send response was lost (transport failure): the outcome is unknown, so the
    // next cycle reconciles them via the provider instead of blindly re-sending.
    public const string MarkAwaitingConfirmation =
        """
        UPDATE dbo.Message
        SET Status = 6                                            -- AwaitingConfirmation
        WHERE Id IN @Ids AND Status = 1;                         -- only ones still Queued
        """;

    // Reconciliation found no provider record: the message was never accepted, so reset it to Queued
    // for a normal (safe) re-send.
    public const string RequeueMessage =
        """
        UPDATE dbo.Message
        SET Status = 1                                            -- Queued
        WHERE Id = @Id AND Status = 6;                           -- AwaitingConfirmation
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
