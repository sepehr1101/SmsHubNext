-- DeliveryReportPoll: the work queue for outbound delivery-report (DLR) polling (Phase 2).
-- One row per Submitted message awaiting a terminal delivery status. Enqueued by the
-- dispatcher the moment a provider accepts a message; dequeued by the poll worker once the
-- message reaches a terminal DeliveryStatus or its provider status window lapses.
--
-- Why a queue and not a scan of Message: Message.DeliveryStatus is deliberately un-indexed
-- (README §4.10/§8.3) so its in-place update never churns an index, and the fact reaches a
-- billion rows. This queue holds only the small set of in-flight messages and is claimed by
-- NextPollAtUtc, so polling never touches the cold fact. It mirrors the dispatch outbox: work
-- is claimed atomically from SQL and survives restarts.

CREATE TABLE dbo.DeliveryReportPoll
(
    MessageId         BIGINT       NOT NULL,
    SubmitDateJalali  CHAR(10)     NOT NULL,                 -- copied from Message: the DeliveryReport partition key
    ProviderId        TINYINT      NOT NULL,                 -- which provider holds the status (routing + forensics)
    ProviderMessageId VARCHAR(50)  NOT NULL,                 -- the id we query the provider with
    DispatchedAtUtc   DATETIME2(3) NOT NULL,                 -- when handed to the provider; anchors the status window
    NextPollAtUtc     DATETIME2(3) NOT NULL,                 -- next time this row is eligible to be polled
    Attempts          INT          NOT NULL CONSTRAINT DF_DeliveryReportPoll_Attempts DEFAULT (0),
    CONSTRAINT PK_DeliveryReportPoll PRIMARY KEY (MessageId),
    CONSTRAINT FK_DeliveryReportPoll_Message FOREIGN KEY (MessageId) REFERENCES dbo.Message (Id)
);
GO

-- Claim path: oldest-due first.
CREATE NONCLUSTERED INDEX IX_DeliveryReportPoll_NextPoll ON dbo.DeliveryReportPoll (NextPollAtUtc);
GO
