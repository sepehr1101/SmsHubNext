-- MessageBatch: the request/accounting header — one row per send API call (README §4.13).
-- Home of who-called/what-it-cost, per-call API-key attribution, batch idempotency,
-- and dispatch-level outcomes (notably provider-credit holds). Far below Message in
-- volume (one row per call, not per recipient).

CREATE TABLE dbo.MessageBatch
(
    Id                   BIGINT        IDENTITY(1,1) NOT NULL,
    SubmitDateJalali     CHAR(10)      NOT NULL,                 -- partition column; yyyy/mm/dd
    ReceivedAtUtc        DATETIME2(3)  NOT NULL CONSTRAINT DF_MessageBatch_ReceivedAtUtc DEFAULT (SYSUTCDATETIME()),
    CustomerId           SMALLINT      NOT NULL,
    ApiKeyId             INT           NOT NULL,                 -- which key made the call (attribution)
    SenderLineId         SMALLINT      NOT NULL,
    ProviderId           TINYINT       NOT NULL,
    ClientBatchId        VARCHAR(100)  NULL,                     -- caller's batch idempotency key
    MessageCount         INT           NOT NULL,                 -- rollup
    SegmentCount         INT           NOT NULL,                 -- rollup
    TotalCost            DECIMAL(19,4) NOT NULL,                 -- rollup (sum of message costs)
    Status               TINYINT       NOT NULL,                 -- 1 Received .. 7 Failed (BatchStatus)
    StatusReason         TINYINT       NULL,                     -- e.g. InsufficientProviderCredit
    ProviderResultCode   INT           NULL,                     -- raw provider submission code
    DispatchStartedAtUtc DATETIME2(3)  NULL,                     -- milestone; - ReceivedAtUtc = queue wait
    FinishedAtUtc        DATETIME2(3)  NULL,                     -- milestone; set on any terminal status
    StatusChangedAtUtc   DATETIME2(3)  NOT NULL CONSTRAINT DF_MessageBatch_StatusChangedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_MessageBatch PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT FK_MessageBatch_Customer   FOREIGN KEY (CustomerId)   REFERENCES dbo.Customer (Id),
    CONSTRAINT FK_MessageBatch_ApiKey     FOREIGN KEY (ApiKeyId)     REFERENCES dbo.ApiKey (Id),
    CONSTRAINT FK_MessageBatch_SenderLine FOREIGN KEY (SenderLineId) REFERENCES dbo.SenderLine (Id),
    CONSTRAINT FK_MessageBatch_Provider   FOREIGN KEY (ProviderId)   REFERENCES dbo.Provider (Id)
);
GO

-- CIX aligns with Jalali-monthly partitioning (added additively later, README §9);
-- OPTIMIZE_FOR_SEQUENTIAL_KEY tames last-page contention on the trailing identity (README §8.2).
CREATE CLUSTERED INDEX CIX_MessageBatch ON dbo.MessageBatch (SubmitDateJalali, Id)
    WITH (OPTIMIZE_FOR_SEQUENTIAL_KEY = ON);
GO

-- "Batches by customer" reporting.
CREATE NONCLUSTERED INDEX IX_MessageBatch_Customer ON dbo.MessageBatch (CustomerId, SubmitDateJalali);
GO

-- Batch idempotency lookups (only when the caller supplied a key).
CREATE NONCLUSTERED INDEX IX_MessageBatch_ClientBatchId ON dbo.MessageBatch (ClientBatchId)
    WHERE ClientBatchId IS NOT NULL;
GO
