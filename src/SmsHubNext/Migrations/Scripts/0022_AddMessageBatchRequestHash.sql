-- Store a canonical SHA-256 fingerprint of the send request payload so ClientBatchId
-- idempotency can reject accidental reuse with different recipients/text/options.

ALTER TABLE dbo.MessageBatch
    ADD RequestHash VARBINARY(32) NULL;
