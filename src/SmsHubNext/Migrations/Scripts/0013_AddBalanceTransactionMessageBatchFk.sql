-- Additive: now that MessageBatch exists, wire the FK promised in 0008 so a debit/refund
-- ledger entry can point at the batch that caused it (README §4.15). Nullable — top-ups
-- and manual adjustments have no batch.

ALTER TABLE dbo.BalanceTransaction
    ADD CONSTRAINT FK_BalanceTransaction_MessageBatch
    FOREIGN KEY (MessageBatchId) REFERENCES dbo.MessageBatch (Id);
GO
