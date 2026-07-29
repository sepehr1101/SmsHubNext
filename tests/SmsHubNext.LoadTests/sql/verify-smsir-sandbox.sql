SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @RunId VARCHAR(40) = '$(RunId)';
DECLARE @ExpectedMessageCount BIGINT = $(ExpectedMessageCount);
DECLARE @ExpectedBatchCount BIGINT = $(ExpectedBatchCount);
DECLARE @VerifyTimeoutMinutes INT = $(VerifyTimeoutMinutes);
DECLARE @ClientBatchPrefix VARCHAR(100) = CONCAT('smsir-sandbox-', @RunId, '-%');
DECLARE @CustomerId SMALLINT = (
    SELECT Id FROM dbo.Customer WHERE Code = 'smsir-sandbox-customer'
);
DECLARE @DeadlineUtc DATETIME2(3) = DATEADD(MINUTE, @VerifyTimeoutMinutes, SYSUTCDATETIME());

IF @CustomerId IS NULL
    THROW 51101, 'sms.ir sandbox customer was not seeded.', 1;

WHILE EXISTS (
    SELECT 1
    FROM dbo.MessageBatch
    WHERE CustomerId = @CustomerId
      AND ClientBatchId LIKE @ClientBatchPrefix
      AND Status IN (1, 2, 5)
) AND SYSUTCDATETIME() < @DeadlineUtc
BEGIN
    WAITFOR DELAY '00:00:01';
END;

IF (SELECT COUNT_BIG(*) FROM dbo.MessageBatch
    WHERE CustomerId = @CustomerId AND ClientBatchId LIKE @ClientBatchPrefix) <> @ExpectedBatchCount
    THROW 51102, 'Invariant failed: sms.ir sandbox batch count differs from the requested count.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.Message m
    INNER JOIN dbo.MessageBatch b ON b.Id = m.MessageBatchId
    WHERE b.CustomerId = @CustomerId AND b.ClientBatchId LIKE @ClientBatchPrefix) <> @ExpectedMessageCount
    THROW 51103, 'Invariant failed: sms.ir sandbox message count differs from the requested count.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.MessageBody body
    INNER JOIN dbo.Message m ON m.Id = body.Id
    INNER JOIN dbo.MessageBatch b ON b.Id = m.MessageBatchId
    WHERE b.CustomerId = @CustomerId AND b.ClientBatchId LIKE @ClientBatchPrefix) <> @ExpectedMessageCount
    THROW 51104, 'Invariant failed: sms.ir sandbox MessageBody count differs from Message count.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.MessageBatch
    WHERE CustomerId = @CustomerId
      AND ClientBatchId LIKE @ClientBatchPrefix
      AND Status <> 3
)
    THROW 51105, 'Invariant failed: one or more sms.ir sandbox batches did not complete dispatch.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.Message m
    INNER JOIN dbo.MessageBatch b ON b.Id = m.MessageBatchId
    WHERE b.CustomerId = @CustomerId
      AND b.ClientBatchId LIKE @ClientBatchPrefix
      AND (m.Status <> 2 OR NULLIF(m.ProviderMessageId, '') IS NULL)
)
    THROW 51106, 'Invariant failed: one or more sms.ir sandbox messages were not submitted with a provider id.', 1;

IF (SELECT COUNT_BIG(DISTINCT m.ClientCorrelatedId)
    FROM dbo.Message m
    INNER JOIN dbo.MessageBatch b ON b.Id = m.MessageBatchId
    WHERE b.CustomerId = @CustomerId AND b.ClientBatchId LIKE @ClientBatchPrefix) <> @ExpectedMessageCount
    THROW 51107, 'Invariant failed: client-correlated ids are not unique across the sandbox run.', 1;

IF EXISTS (
    SELECT b.Id
    FROM dbo.MessageBatch b
    LEFT JOIN dbo.Message m ON m.MessageBatchId = b.Id
    WHERE b.CustomerId = @CustomerId AND b.ClientBatchId LIKE @ClientBatchPrefix
    GROUP BY b.Id, b.MessageCount, b.TotalCost
    HAVING COUNT_BIG(m.Id) <> b.MessageCount
        OR COALESCE(SUM(m.TotalCost), 0) <> b.TotalCost
)
    THROW 51108, 'Invariant failed: batch accounting differs from its messages.', 1;

IF EXISTS (
    SELECT b.Id
    FROM dbo.MessageBatch b
    LEFT JOIN dbo.BalanceTransaction bt
      ON bt.MessageBatchId = b.Id AND bt.Type = 2
    WHERE b.CustomerId = @CustomerId AND b.ClientBatchId LIKE @ClientBatchPrefix
    GROUP BY b.Id, b.TotalCost
    HAVING COUNT_BIG(bt.Id) <> 1
        OR COALESCE(SUM(bt.Amount), 0) <> -b.TotalCost
)
    THROW 51109, 'Invariant failed: a sandbox batch has a missing, duplicate, or incorrect debit.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.BalanceTransaction bt
    INNER JOIN dbo.MessageBatch b ON b.Id = bt.MessageBatchId
    WHERE b.CustomerId = @CustomerId
      AND b.ClientBatchId LIKE @ClientBatchPrefix
      AND bt.Type = 3
)
    THROW 51110, 'Invariant failed: a successfully submitted sandbox message was refunded.', 1;

IF EXISTS (
    SELECT cb.CustomerId
    FROM dbo.CustomerBalance cb
    INNER JOIN dbo.BalanceTransaction bt ON bt.CustomerId = cb.CustomerId
    WHERE cb.CustomerId = @CustomerId
    GROUP BY cb.CustomerId, cb.Balance
    HAVING cb.Balance <> SUM(bt.Amount)
)
    THROW 51111, 'Invariant failed: balance differs from the append-only ledger sum.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.DeliveryReportPoll poll
    INNER JOIN dbo.Message m ON m.Id = poll.MessageId
    INNER JOIN dbo.MessageBatch b ON b.Id = m.MessageBatchId
    WHERE b.CustomerId = @CustomerId AND b.ClientBatchId LIKE @ClientBatchPrefix) <> @ExpectedMessageCount
    THROW 51112, 'Invariant failed: submitted messages were not all placed in the DLR queue.', 1;

SELECT (
    SELECT
        @RunId AS runId,
        @ExpectedBatchCount AS expectedBatchCount,
        @ExpectedMessageCount AS expectedMessageCount,
        COUNT_BIG(DISTINCT b.Id) AS batchCount,
        COUNT_BIG(DISTINCT m.Id) AS messageCount,
        COUNT_BIG(DISTINCT body.Id) AS messageBodyCount,
        COUNT_BIG(DISTINCT poll.MessageId) AS deliveryPollCount,
        (
            SELECT SUM(CONVERT(BIGINT, attemptBatch.DispatchAttemptCount))
            FROM dbo.MessageBatch attemptBatch
            WHERE attemptBatch.CustomerId = @CustomerId
              AND attemptBatch.ClientBatchId LIKE @ClientBatchPrefix
        ) AS dispatchAttemptCount,
        SUM(m.TotalCost) AS totalMessageCost,
        MIN(b.ReceivedAtUtc) AS firstReceivedAtUtc,
        MAX(b.FinishedAtUtc) AS lastFinishedAtUtc,
        DATEDIFF(MILLISECOND, MIN(b.ReceivedAtUtc), MAX(b.FinishedAtUtc)) AS dispatchWindowMilliseconds,
        (
            SELECT COUNT_BIG(*)
            FROM dbo.MessageBatchEvent e
            INNER JOIN dbo.MessageBatch rateBatch ON rateBatch.Id = e.MessageBatchId
            WHERE rateBatch.CustomerId = @CustomerId
              AND rateBatch.ClientBatchId LIKE @ClientBatchPrefix
              AND e.ProviderResultCode IN (20, 429)
        ) AS rateLimitEventCount,
        (
            SELECT cb.Balance
            FROM dbo.CustomerBalance cb
            WHERE cb.CustomerId = @CustomerId
        ) AS balanceAfter
    FROM dbo.MessageBatch b
    INNER JOIN dbo.Message m ON m.MessageBatchId = b.Id
    INNER JOIN dbo.MessageBody body ON body.Id = m.Id
    INNER JOIN dbo.DeliveryReportPoll poll ON poll.MessageId = m.Id
    WHERE b.CustomerId = @CustomerId
      AND b.ClientBatchId LIKE @ClientBatchPrefix
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
) AS JsonResult;
