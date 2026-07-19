SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @DeadlineUtc DATETIME2(3) = DATEADD(SECOND, 90, SYSUTCDATETIME());
DECLARE @CustomerId SMALLINT = (
    SELECT Id FROM dbo.Customer WHERE Code = 'load-test-customer'
);

WHILE EXISTS (
    SELECT 1
    FROM dbo.MessageBatch
    WHERE CustomerId = @CustomerId
      AND Status IN (1, 2)
) AND SYSUTCDATETIME() < @DeadlineUtc
BEGIN
    WAITFOR DELAY '00:00:01';
END;

IF EXISTS (
    SELECT ClientBatchId
    FROM dbo.MessageBatch
    WHERE CustomerId = @CustomerId
    GROUP BY ClientBatchId
    HAVING COUNT_BIG(*) > 1
)
    THROW 51000, 'Invariant failed: duplicate MessageBatch rows exist for a ClientBatchId.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.MessageBatch
    WHERE CustomerId = @CustomerId AND ClientBatchId = 'load-idempotency-shared') <> 1
    THROW 51001, 'Invariant failed: idempotency storm did not produce exactly one batch.', 1;

IF EXISTS (
    SELECT b.Id
    FROM dbo.MessageBatch b
    LEFT JOIN dbo.Message m ON m.MessageBatchId = b.Id
    WHERE b.CustomerId = @CustomerId
    GROUP BY b.Id, b.MessageCount, b.TotalCost
    HAVING COUNT_BIG(m.Id) <> b.MessageCount
        OR COALESCE(SUM(m.TotalCost), 0) <> b.TotalCost
)
    THROW 51002, 'Invariant failed: MessageBatch accounting does not match its messages.', 1;

IF EXISTS (
    SELECT b.Id
    FROM dbo.MessageBatch b
    LEFT JOIN dbo.BalanceTransaction bt
      ON bt.MessageBatchId = b.Id AND bt.Type = 2
    WHERE b.CustomerId = @CustomerId
    GROUP BY b.Id, b.TotalCost
    HAVING COUNT_BIG(bt.Id) <> 1
        OR COALESCE(SUM(bt.Amount), 0) <> -b.TotalCost
)
    THROW 51003, 'Invariant failed: a batch has a missing, duplicate, or incorrect debit.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.CustomerBalance
    WHERE CustomerId = @CustomerId AND Balance < 0
)
    THROW 51004, 'Invariant failed: customer balance became negative.', 1;

IF EXISTS (
    SELECT cb.CustomerId
    FROM dbo.CustomerBalance cb
    INNER JOIN dbo.BalanceTransaction bt ON bt.CustomerId = cb.CustomerId
    WHERE cb.CustomerId = @CustomerId
    GROUP BY cb.CustomerId, cb.Balance
    HAVING cb.Balance <> SUM(bt.Amount)
)
    THROW 51005, 'Invariant failed: balance does not equal the append-only ledger sum.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.MessageBatch
    WHERE CustomerId = @CustomerId AND Status IN (1, 2)
)
    THROW 51006, 'Invariant failed: the dispatch queue did not drain before the recovery deadline.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.MessageBatch
    WHERE CustomerId = @CustomerId AND Status <> 3
)
    THROW 51007, 'Invariant failed: a Loopback load-test batch did not complete successfully.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.MessageBatch
    WHERE CustomerId = @CustomerId
      AND Status = 2
      AND DispatchLeaseExpiresAtUtc <= SYSUTCDATETIME()
)
    THROW 51008, 'Invariant failed: an expired Dispatching lease remained stuck.', 1;

SELECT
    COUNT_BIG(DISTINCT b.Id) AS BatchCount,
    COUNT_BIG(DISTINCT m.Id) AS MessageCount,
    SUM(m.TotalCost) AS TotalMessageCost,
    MIN(cb.Balance) AS BalanceAfter
FROM dbo.MessageBatch b
INNER JOIN dbo.Message m ON m.MessageBatchId = b.Id
INNER JOIN dbo.CustomerBalance cb ON cb.CustomerId = b.CustomerId
WHERE b.CustomerId = @CustomerId;

PRINT 'All SmsHubNext load-test invariants passed.';
