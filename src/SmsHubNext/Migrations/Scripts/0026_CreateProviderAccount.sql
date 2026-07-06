-- ProviderAccount: provider-agnostic account authentication data.
-- Non-sensitive settings stay as JSON; the sensitive secret is encrypted by the app.

CREATE TABLE dbo.ProviderAccount
(
    Id              INT             IDENTITY(1,1) NOT NULL,
    ProviderId      TINYINT         NOT NULL,
    DisplayName     NVARCHAR(100)   NOT NULL,
    AuthType        VARCHAR(50)     NOT NULL,
    SettingsJson    NVARCHAR(MAX)   NOT NULL CONSTRAINT DF_ProviderAccount_SettingsJson DEFAULT (N'{}'),
    SecretEncrypted VARBINARY(MAX)  NOT NULL,
    IsActive        BIT             NOT NULL CONSTRAINT DF_ProviderAccount_IsActive DEFAULT (1),
    CreatedAtUtc    DATETIME2(3)    NOT NULL CONSTRAINT DF_ProviderAccount_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc    DATETIME2(3)    NOT NULL CONSTRAINT DF_ProviderAccount_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ProviderAccount PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ProviderAccount_Provider FOREIGN KEY (ProviderId) REFERENCES dbo.Provider (Id),
    CONSTRAINT CK_ProviderAccount_SettingsJson_IsJson CHECK (ISJSON(SettingsJson) = 1)
);
GO

CREATE NONCLUSTERED INDEX IX_ProviderAccount_Provider_Active
    ON dbo.ProviderAccount (ProviderId, IsActive)
    INCLUDE (DisplayName, AuthType, UpdatedAtUtc);
GO

ALTER TABLE dbo.SenderLine
    ADD ProviderAccountId INT NULL;
GO

ALTER TABLE dbo.SenderLine
    ADD CONSTRAINT FK_SenderLine_ProviderAccount
    FOREIGN KEY (ProviderAccountId) REFERENCES dbo.ProviderAccount (Id);
GO

CREATE NONCLUSTERED INDEX IX_SenderLine_ProviderAccount
    ON dbo.SenderLine (ProviderAccountId)
    WHERE ProviderAccountId IS NOT NULL;
GO
