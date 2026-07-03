-- Enforce batch-level idempotency per customer. A caller retrying the same ClientBatchId
-- must resolve to the existing MessageBatch instead of creating a second debit/send.

DROP INDEX IX_MessageBatch_ClientBatchId ON dbo.MessageBatch;
GO

CREATE UNIQUE NONCLUSTERED INDEX UX_MessageBatch_Customer_ClientBatchId
    ON dbo.MessageBatch (CustomerId, ClientBatchId)
    WHERE ClientBatchId IS NOT NULL;
GO
