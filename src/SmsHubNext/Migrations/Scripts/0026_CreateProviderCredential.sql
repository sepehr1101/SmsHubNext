-- ProviderCredential: encrypted provider secrets (README 4.17).
-- SQL Server stores ciphertext only; the app protects the payload with ASP.NET Core Data Protection.

CREATE TABLE dbo.ProviderCredential
(
    Id             INT            IDENTITY(1,1) NOT NULL,
    ProviderId     TINYINT        NOT NULL,
    CredentialKey  VARCHAR(100)   NOT NULL,
    SecretCipher   VARBINARY(MAX) NOT NULL,
    UpdatedAtUtc   DATETIME2(3)   NOT NULL CONSTRAINT DF_ProviderCredential_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ProviderCredential PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ProviderCredential_Provider FOREIGN KEY (ProviderId) REFERENCES dbo.Provider (Id)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX UX_ProviderCredential_Provider_Key
    ON dbo.ProviderCredential (ProviderId, CredentialKey);
GO
