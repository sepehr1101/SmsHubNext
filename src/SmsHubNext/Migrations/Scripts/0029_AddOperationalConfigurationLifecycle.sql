-- Lifecycle metadata for mutable operational configuration.
-- Business/event facts remain append-only; these rows are retained for audit.

ALTER TABLE dbo.Tariff ADD
    DeletedAtUtc      DATETIME2(3) NULL,
    DeletedByApiKeyId INT          NULL;
GO

ALTER TABLE dbo.ProviderAccount ADD
    DeletedAtUtc      DATETIME2(3) NULL,
    DeletedByApiKeyId INT          NULL;
GO

ALTER TABLE dbo.ApiKeyIpRestriction ADD
    UpdatedAtUtc      DATETIME2(3) NOT NULL
        CONSTRAINT DF_ApiKeyIpRestriction_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
    DeletedAtUtc      DATETIME2(3) NULL,
    DeletedByApiKeyId INT          NULL;
GO

ALTER TABLE dbo.ApiKey ADD RevokedByApiKeyId INT NULL;
GO

ALTER TABLE dbo.Tariff ADD
    CONSTRAINT FK_Tariff_DeletedByApiKey FOREIGN KEY (DeletedByApiKeyId) REFERENCES dbo.ApiKey (Id),
    CONSTRAINT CK_Tariff_DeleteAudit CHECK
        ((DeletedAtUtc IS NULL AND DeletedByApiKeyId IS NULL) OR
         (DeletedAtUtc IS NOT NULL AND DeletedByApiKeyId IS NOT NULL));
GO

ALTER TABLE dbo.ProviderAccount ADD
    CONSTRAINT FK_ProviderAccount_DeletedByApiKey FOREIGN KEY (DeletedByApiKeyId) REFERENCES dbo.ApiKey (Id),
    CONSTRAINT CK_ProviderAccount_DeleteAudit CHECK
        ((DeletedAtUtc IS NULL AND DeletedByApiKeyId IS NULL) OR
         (DeletedAtUtc IS NOT NULL AND DeletedByApiKeyId IS NOT NULL));
GO

ALTER TABLE dbo.ApiKeyIpRestriction ADD
    CONSTRAINT FK_ApiKeyIpRestriction_DeletedByApiKey
        FOREIGN KEY (DeletedByApiKeyId) REFERENCES dbo.ApiKey (Id),
    CONSTRAINT CK_ApiKeyIpRestriction_DeleteAudit CHECK
        ((DeletedAtUtc IS NULL AND DeletedByApiKeyId IS NULL) OR
         (DeletedAtUtc IS NOT NULL AND DeletedByApiKeyId IS NOT NULL));
GO

ALTER TABLE dbo.ApiKey ADD
    CONSTRAINT FK_ApiKey_RevokedByApiKey FOREIGN KEY (RevokedByApiKeyId) REFERENCES dbo.ApiKey (Id),
    CONSTRAINT CK_ApiKey_RevokeAudit CHECK
        (RevokedAtUtc IS NOT NULL OR RevokedByApiKeyId IS NULL);
GO
