-- InboundMessage: incoming (MO) messages pulled from a provider's inbox (Phase 4).
-- Providers expose inbound messages via a destructive pull (fetching dequeues them at the
-- provider), so each row is persisted the moment it is fetched. Low volume relative to the
-- outbound fact, so it is a plain table (no Jalali partitioning). Attribution to a customer is
-- additive later; for now the raw provider fields plus the receiving number are kept.

CREATE TABLE dbo.InboundMessage
(
    Id                BIGINT        IDENTITY(1,1) NOT NULL,
    ProviderId        TINYINT       NOT NULL,                 -- which provider delivered it
    SenderNumber      VARCHAR(20)   NOT NULL,                 -- the external mobile that sent the message
    RecipientNumber   VARCHAR(20)   NOT NULL,                 -- our number that received it
    Body              NVARCHAR(MAX) NOT NULL,                 -- the message text (UTF-8 / Persian)
    ProviderTimestamp VARCHAR(25)   NULL,                     -- provider's reported time, verbatim (tz not specified)
    ReceivedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_InboundMessage_ReceivedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_InboundMessage PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT FK_InboundMessage_Provider FOREIGN KEY (ProviderId) REFERENCES dbo.Provider (Id)
);
GO

-- Newest-first scans of the whole inbox.
CREATE CLUSTERED INDEX CIX_InboundMessage ON dbo.InboundMessage (ReceivedAtUtc DESC, Id DESC);
GO

-- A receiving number's inbox history.
CREATE NONCLUSTERED INDEX IX_InboundMessage_Recipient ON dbo.InboundMessage (RecipientNumber, ReceivedAtUtc DESC);
GO
