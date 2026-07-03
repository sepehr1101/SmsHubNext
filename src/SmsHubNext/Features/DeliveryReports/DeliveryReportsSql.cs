namespace SmsHubNext.Features.DeliveryReports;

internal static class DeliveryReportsSql
{
    // Fetch the message's partition key (and prove it exists) before recording a report.
    public const string GetMessagePartition =
        "SELECT SubmitDateJalali, MessageBatchId FROM dbo.Message WHERE Id = @MessageId;";

    public const string InsertReport =
        """
        INSERT INTO dbo.DeliveryReport (SubmitDateJalali, MessageId, NormalizedStatus, RawStatusCode, ReceivedAtUtc)
        OUTPUT INSERTED.Id
        VALUES (@SubmitDateJalali, @MessageId, @NormalizedStatus, @RawStatusCode, @ReceivedAtUtc);
        """;

    // Update the denormalized read model in place (README §4.10/§8.5). DeliveredAtUtc is
    // stamped only when the message becomes Delivered, and is never cleared by a later report.
    public const string UpdateMessageStatus =
        """
        UPDATE dbo.Message
        SET DeliveryStatus = @DeliveryStatus,
            DeliveredAtUtc = CASE WHEN @DeliveryStatus = @DeliveredValue THEN @ReceivedAtUtc ELSE DeliveredAtUtc END
        WHERE Id = @MessageId;
        """;

    public const string InsertBatchEventForReport =
        """
        INSERT INTO dbo.MessageBatchEvent (MessageBatchId, EventTimeUtc, EventType, Detail)
        VALUES (@MessageBatchId, @ReceivedAtUtc, @EventType, @Detail);
        """;

    public const string ListByMessage =
        """
        SELECT Id, SubmitDateJalali, MessageId, NormalizedStatus, RawStatusCode, ReceivedAtUtc
        FROM dbo.DeliveryReport
        WHERE MessageId = @MessageId
        ORDER BY ReceivedAtUtc DESC, Id DESC;
        """;

    // --- DLR polling queue (Phase 2) -------------------------------------------------------

    // Atomically claim the due poll rows and lease them forward (NextPollAtUtc) so a slow cycle
    // or a second worker doesn't re-claim them. READPAST/UPDLOCK let workers skip each other's
    // in-flight rows. Terminal rows are deleted after processing; in-flight rows keep the lease.
    public const string ClaimDuePolls =
        """
        ;WITH due AS (
            SELECT TOP (@BatchSize) MessageId
            FROM dbo.DeliveryReportPoll WITH (READPAST, UPDLOCK, ROWLOCK)
            WHERE NextPollAtUtc <= @Now
            ORDER BY NextPollAtUtc
        )
        UPDATE p
        SET NextPollAtUtc = @NextPollAtUtc, Attempts = p.Attempts + 1
        OUTPUT INSERTED.MessageId, INSERTED.SubmitDateJalali, INSERTED.ProviderId,
               INSERTED.ProviderMessageId, INSERTED.DispatchedAtUtc, INSERTED.Attempts
        FROM dbo.DeliveryReportPoll p
        INNER JOIN due ON due.MessageId = p.MessageId;
        """;

    public const string GetProviderCode =
        "SELECT Code FROM dbo.Provider WHERE Id = @ProviderId;";

    // Staging table for a cycle's terminal outcomes, bulk-loaded (SqlBulkCopy) before ApplyTerminalReports.
    // Session-scoped #temp on the poller's connection; dropped when that connection closes.
    public const string CreateTerminalApplyTemp =
        """
        CREATE TABLE #TerminalApply
        (
            MessageId        BIGINT       NOT NULL PRIMARY KEY,
            SubmitDateJalali CHAR(10)     NOT NULL,
            DeliveryStatus   TINYINT      NOT NULL,   -- read-model projection (Message.DeliveryStatus)
            NormalizedStatus TINYINT      NOT NULL,   -- DeliveryReportStatus for the history row
            RawStatusCode    INT          NOT NULL,
            ReceivedAtUtc    DATETIME2(3) NOT NULL
        );
        """;

    // Apply a whole batch of terminal outcomes set-based and idempotently, in one transaction:
    // project onto the Message read model (guarded on Pending so only the first terminal per message
    // wins and stamps DeliveredAtUtc), append a DeliveryReport for exactly those just-transitioned
    // messages, then dequeue every applied message. A duplicate (already-terminal) row updates nothing,
    // inserts no history, and is still dequeued.
    public const string ApplyTerminalReports =
        """
        DECLARE @Transitioned TABLE (Id BIGINT NOT NULL PRIMARY KEY);

        UPDATE m
        SET DeliveryStatus = a.DeliveryStatus,
            DeliveredAtUtc = CASE WHEN a.DeliveryStatus = @DeliveredValue THEN a.ReceivedAtUtc ELSE m.DeliveredAtUtc END
        OUTPUT INSERTED.Id INTO @Transitioned (Id)
        FROM dbo.Message m
        INNER JOIN #TerminalApply a ON a.MessageId = m.Id
        WHERE m.DeliveryStatus = @PendingValue;

        INSERT INTO dbo.DeliveryReport (SubmitDateJalali, MessageId, NormalizedStatus, RawStatusCode, ReceivedAtUtc)
        SELECT a.SubmitDateJalali, a.MessageId, a.NormalizedStatus, a.RawStatusCode, a.ReceivedAtUtc
        FROM #TerminalApply a
        INNER JOIN @Transitioned t ON t.Id = a.MessageId;

        INSERT INTO dbo.MessageBatchEvent (MessageBatchId, EventTimeUtc, EventType, Detail)
        SELECT
            m.MessageBatchId,
            MAX(a.ReceivedAtUtc),
            @DeliveryUpdatedEventType,
            CONCAT(
                'Delivery reports applied: ', COUNT_BIG(*),
                ' terminal message(s); delivered=', SUM(CASE WHEN a.DeliveryStatus = @DeliveredValue THEN 1 ELSE 0 END),
                ', undelivered=', SUM(CASE WHEN a.DeliveryStatus = 3 THEN 1 ELSE 0 END),
                ', expired=', SUM(CASE WHEN a.DeliveryStatus = 4 THEN 1 ELSE 0 END),
                ', unknown=', SUM(CASE WHEN a.DeliveryStatus = 5 THEN 1 ELSE 0 END),
                '.'
            )
        FROM #TerminalApply a
        INNER JOIN @Transitioned t ON t.Id = a.MessageId
        INNER JOIN dbo.Message m ON m.Id = a.MessageId
        GROUP BY m.MessageBatchId;

        DELETE p
        FROM dbo.DeliveryReportPoll p
        INNER JOIN #TerminalApply a ON a.MessageId = p.MessageId;
        """;
}
