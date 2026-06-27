-- ApiKey: per-customer API keys. Stored as SHA-256 hash only, never plaintext (README §4.2).

CREATE TABLE dbo.ApiKey
(
    Id            INT           IDENTITY(1,1) NOT NULL,
    CustomerId    SMALLINT      NOT NULL,
    Name          NVARCHAR(100) NOT NULL,
    KeyPrefix     VARCHAR(12)   NOT NULL,
    KeyHash       BINARY(32)    NOT NULL,
    IsActive      BIT           NOT NULL CONSTRAINT DF_ApiKey_IsActive DEFAULT (1),
    ExpiresAtUtc  DATETIME2(3)  NULL,
    RevokedAtUtc  DATETIME2(3)  NULL,
    LastUsedAtUtc DATETIME2(3)  NULL,
    CreatedAtUtc  DATETIME2(3)  NOT NULL CONSTRAINT DF_ApiKey_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ApiKey PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ApiKey_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customer (Id)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX UX_ApiKey_KeyHash ON dbo.ApiKey (KeyHash);
GO

CREATE NONCLUSTERED INDEX IX_ApiKey_KeyPrefix ON dbo.ApiKey (KeyPrefix);
GO

CREATE NONCLUSTERED INDEX IX_ApiKey_CustomerId ON dbo.ApiKey (CustomerId);
GO
