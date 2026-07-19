-- Add an explicit renewable/fenced dispatch lease. StatusChangedAtUtc remains the business
-- transition timestamp; lease ownership and expiry are operational concurrency state.

ALTER TABLE dbo.MessageBatch
    ADD DispatchLeaseToken UNIQUEIDENTIFIER NULL,
        DispatchLeaseExpiresAtUtc DATETIME2(3) NULL;
GO

CREATE NONCLUSTERED INDEX IX_MessageBatch_DispatchLease
    ON dbo.MessageBatch (Status, DispatchLeaseExpiresAtUtc)
    WHERE Status = 2;
GO
