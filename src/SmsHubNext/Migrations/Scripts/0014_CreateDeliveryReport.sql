-- DeliveryReport: append-only status-event history for a message (README §4.12).
-- The full audit trail (every raw provider report, multiple per message) behind the
-- denormalized Message.DeliveryStatus read model — used for forensics, disputes, and
-- re-deriving the projection. Partitioned by the message's SubmitDateJalali so a message
-- and its reports stay co-located for lockstep retention.

CREATE TABLE dbo.DeliveryReport
(
    Id               BIGINT       IDENTITY(1,1) NOT NULL,
    SubmitDateJalali CHAR(10)     NOT NULL,                 -- partition column, copied from the message
    MessageId        BIGINT       NOT NULL,
    NormalizedStatus TINYINT      NOT NULL,                 -- DeliveryReportStatus: Delivered/Undelivered/Expired/Rejected/Unknown
    RawStatusCode    INT          NOT NULL,                 -- provider-native code
    ReceivedAtUtc    DATETIME2(3) NOT NULL CONSTRAINT DF_DeliveryReport_ReceivedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_DeliveryReport PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT FK_DeliveryReport_Message FOREIGN KEY (MessageId) REFERENCES dbo.Message (Id)
);
GO

-- Partition-aligned; clusters a message's reports together, newest first, for history reads.
CREATE CLUSTERED INDEX CIX_DeliveryReport ON dbo.DeliveryReport (SubmitDateJalali, MessageId, ReceivedAtUtc DESC);
GO
