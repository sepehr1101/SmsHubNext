-- Durable acceptance evidence. No backfill: old poll rows are not reliable history.
CREATE TABLE dbo.DispatchAcceptance
(
    Id BIGINT NOT NULL CONSTRAINT PK_DispatchAcceptance PRIMARY KEY,
    AcceptedAtUtc DATETIME2(3) NOT NULL,
    ProviderId TINYINT NOT NULL
);
CREATE INDEX IX_DispatchAcceptance_Time ON dbo.DispatchAcceptance(AcceptedAtUtc) INCLUDE(ProviderId);
