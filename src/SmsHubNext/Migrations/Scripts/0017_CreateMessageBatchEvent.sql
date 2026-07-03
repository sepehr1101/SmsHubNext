-- MessageBatchEvent: append-only operational timeline for a batch (README §4.16).
-- This is not business audit; it answers operator questions such as "why is this
-- batch held?", "was it retried?", and "which provider outcome changed its state?".

CREATE TABLE dbo.MessageBatchEvent
(
    Id                 BIGINT        IDENTITY(1,1) NOT NULL,
    MessageBatchId     BIGINT        NOT NULL,
    EventTimeUtc       DATETIME2(3)  NOT NULL CONSTRAINT DF_MessageBatchEvent_EventTimeUtc DEFAULT (SYSUTCDATETIME()),
    EventType          TINYINT       NOT NULL,
    BatchStatus        TINYINT       NULL,
    BatchStatusReason  TINYINT       NULL,
    ProviderResultCode INT           NULL,
    Detail             NVARCHAR(500) NULL,
    CONSTRAINT PK_MessageBatchEvent PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT FK_MessageBatchEvent_MessageBatch FOREIGN KEY (MessageBatchId) REFERENCES dbo.MessageBatch (Id)
);
GO

-- A batch's timeline, oldest first.
CREATE CLUSTERED INDEX CIX_MessageBatchEvent ON dbo.MessageBatchEvent (MessageBatchId, EventTimeUtc, Id);
GO
