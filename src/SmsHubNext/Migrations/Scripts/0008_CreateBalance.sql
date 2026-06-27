-- Prepaid balance (CustomerBalance) + append-only money ledger (BalanceTransaction).
-- README §4.14/§4.15.

CREATE TABLE dbo.CustomerBalance
(
    CustomerId   SMALLINT      NOT NULL,
    Balance      DECIMAL(19,4) NOT NULL CONSTRAINT DF_CustomerBalance_Balance DEFAULT (0),
    UpdatedAtUtc DATETIME2(3)  NOT NULL CONSTRAINT DF_CustomerBalance_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_CustomerBalance PRIMARY KEY CLUSTERED (CustomerId),
    CONSTRAINT FK_CustomerBalance_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customer (Id)
);
GO

CREATE TABLE dbo.BalanceTransaction
(
    Id             BIGINT        IDENTITY(1,1) NOT NULL,
    CustomerId     SMALLINT      NOT NULL,
    Type           TINYINT       NOT NULL,       -- 1 TopUp, 2 Debit, 3 Refund, 4 Adjustment
    Amount         DECIMAL(19,4) NOT NULL,       -- signed: + credit, - debit
    BalanceAfter   DECIMAL(19,4) NOT NULL,
    MessageBatchId BIGINT        NULL,           -- FK added with MessageBatch (additive, later)
    Reference      VARCHAR(100)  NULL,
    CreatedAtUtc   DATETIME2(3)  NOT NULL CONSTRAINT DF_BalanceTransaction_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_BalanceTransaction PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT FK_BalanceTransaction_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customer (Id)
);
GO

-- Clusters each tenant's ledger together, append-ordered (README §4.15).
CREATE CLUSTERED INDEX CIX_BalanceTransaction ON dbo.BalanceTransaction (CustomerId, Id);
GO
