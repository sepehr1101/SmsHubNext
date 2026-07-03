-- Bounded dispatch retries for transient provider/transport failures. This keeps a batch from
-- cycling forever without an operator-visible terminal state.

ALTER TABLE dbo.MessageBatch
    ADD DispatchAttemptCount INT NOT NULL
        CONSTRAINT DF_MessageBatch_DispatchAttemptCount DEFAULT (0);
GO
