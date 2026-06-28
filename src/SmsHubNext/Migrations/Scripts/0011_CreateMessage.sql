-- Message: the central high-volume fact + delivery read model (README §4.10).
-- Narrow and fixed-width on purpose: text lives in MessageBody, status history in
-- DeliveryReport. Carries denormalized dimension keys and a frozen cost snapshot so
-- reports never join the billion-row fact nor re-resolve tariffs.

CREATE TABLE dbo.Message
(
    Id                 BIGINT        IDENTITY(1,1) NOT NULL,
    SubmitDateJalali   CHAR(10)      NOT NULL,                 -- partition column; yyyy/mm/dd; period key
    MessageBatchId     BIGINT        NOT NULL,                 -- the API call this message belongs to
    SubmittedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Message_SubmittedAtUtc DEFAULT (SYSUTCDATETIME()),
    CustomerId         SMALLINT      NOT NULL,                 -- denormalized for join-free reporting
    ProviderId         TINYINT       NOT NULL,
    SenderLineId       SMALLINT      NOT NULL,
    MessageTypeId      TINYINT       NOT NULL,                 -- delivery class + business purpose
    GeoSectionId       INT           NULL,                     -- caller-supplied; nullable
    MobileNumber       VARCHAR(15)   NOT NULL,                 -- recipient (ad-hoc)
    ClientCorrelatedId VARCHAR(100)  NULL,                     -- caller id / idempotency key
    BillId             VARCHAR(31)   NULL,                     -- external bill reference
    PayId              VARCHAR(31)   NULL,                     -- external payment reference
    Encoding           TINYINT       NOT NULL,                 -- GSM7 / UCS2 (snapshot)
    CharacterCount     SMALLINT      NOT NULL,                 -- snapshot
    SegmentCount       TINYINT       NOT NULL,                 -- parts (snapshot)
    TariffId           INT           NOT NULL,                 -- which tariff priced this (audit)
    UnitPrice          DECIMAL(19,4) NOT NULL,                 -- per-segment price at submission (snapshot)
    TotalCost          DECIMAL(19,4) NOT NULL,                 -- UnitPrice * SegmentCount (snapshot)
    Status             TINYINT       NOT NULL,                 -- send lifecycle (SendStatus): 1 Queued ..
    DeliveryStatus     TINYINT       NOT NULL,                 -- current delivery state (DeliveryStatus): 1 Pending ..
    DeliveredAtUtc     DATETIME2(3)  NULL,                     -- set when DeliveryStatus -> Delivered
    ProviderMessageId  VARCHAR(50)   NULL,                     -- provider id; key to match incoming DLRs
    CONSTRAINT PK_Message PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT FK_Message_MessageBatch FOREIGN KEY (MessageBatchId) REFERENCES dbo.MessageBatch (Id),
    CONSTRAINT FK_Message_Customer     FOREIGN KEY (CustomerId)     REFERENCES dbo.Customer (Id),
    CONSTRAINT FK_Message_Provider     FOREIGN KEY (ProviderId)     REFERENCES dbo.Provider (Id),
    CONSTRAINT FK_Message_SenderLine   FOREIGN KEY (SenderLineId)   REFERENCES dbo.SenderLine (Id),
    CONSTRAINT FK_Message_MessageType  FOREIGN KEY (MessageTypeId)  REFERENCES dbo.MessageType (Id),
    CONSTRAINT FK_Message_GeoSection   FOREIGN KEY (GeoSectionId)   REFERENCES dbo.GeoSection (Id),
    CONSTRAINT FK_Message_Tariff       FOREIGN KEY (TariffId)       REFERENCES dbo.Tariff (Id)
);
GO

-- CIX aligns with Jalali-monthly partitioning (added additively later, README §9);
-- OPTIMIZE_FOR_SEQUENTIAL_KEY tames last-page contention on the trailing identity (README §8.2).
CREATE CLUSTERED INDEX CIX_Message ON dbo.Message (SubmitDateJalali, Id)
    WITH (OPTIMIZE_FOR_SEQUENTIAL_KEY = ON);
GO

-- Recipient history.
CREATE NONCLUSTERED INDEX IX_Message_Mobile ON dbo.Message (MobileNumber, SubmitDateJalali);
GO

-- Resolve an incoming DLR to its message Id.
CREATE NONCLUSTERED INDEX IX_Message_ProviderMessageId ON dbo.Message (ProviderId, ProviderMessageId);
GO

-- Idempotency + client lookups (filtered: only when supplied).
CREATE NONCLUSTERED INDEX IX_Message_ClientCorrelatedId ON dbo.Message (CustomerId, ClientCorrelatedId)
    WHERE ClientCorrelatedId IS NOT NULL;
GO

-- Bill history (filtered: only when supplied).
CREATE NONCLUSTERED INDEX IX_Message_BillId ON dbo.Message (BillId)
    WHERE BillId IS NOT NULL;
GO

-- DeliveryStatus is deliberately left un-indexed (README §4.10/§8.3): its in-place
-- update on the hot partition must never move a row or churn an index; the join-free
-- success rate comes from the cold-partition nonclustered columnstore (added later).
