-- Reference-data rows are retained for historical facts and accounting records.
-- DeletedAtUtc is the soft-delete marker; DeletedByApiKeyId attributes the action
-- to the authenticated API key that performed it.

ALTER TABLE dbo.Customer ADD
    DeletedAtUtc      DATETIME2(3) NULL,
    DeletedByApiKeyId INT          NULL;
GO

ALTER TABLE dbo.Provider ADD
    DeletedAtUtc      DATETIME2(3) NULL,
    DeletedByApiKeyId INT          NULL;
GO

ALTER TABLE dbo.SenderLine ADD
    DeletedAtUtc      DATETIME2(3) NULL,
    DeletedByApiKeyId INT          NULL;
GO

ALTER TABLE dbo.MessageType ADD
    IsActive          BIT          NOT NULL CONSTRAINT DF_MessageType_IsActive DEFAULT (1),
    DeletedAtUtc      DATETIME2(3) NULL,
    DeletedByApiKeyId INT          NULL;
GO

ALTER TABLE dbo.GeoSection ADD
    DeletedAtUtc      DATETIME2(3) NULL,
    DeletedByApiKeyId INT          NULL;
GO

ALTER TABLE dbo.Customer ADD
    CONSTRAINT FK_Customer_DeletedByApiKey FOREIGN KEY (DeletedByApiKeyId) REFERENCES dbo.ApiKey (Id),
    CONSTRAINT CK_Customer_DeleteAudit CHECK
        ((DeletedAtUtc IS NULL AND DeletedByApiKeyId IS NULL) OR
         (DeletedAtUtc IS NOT NULL AND DeletedByApiKeyId IS NOT NULL));
GO

ALTER TABLE dbo.Provider ADD
    CONSTRAINT FK_Provider_DeletedByApiKey FOREIGN KEY (DeletedByApiKeyId) REFERENCES dbo.ApiKey (Id),
    CONSTRAINT CK_Provider_DeleteAudit CHECK
        ((DeletedAtUtc IS NULL AND DeletedByApiKeyId IS NULL) OR
         (DeletedAtUtc IS NOT NULL AND DeletedByApiKeyId IS NOT NULL));
GO

ALTER TABLE dbo.SenderLine ADD
    CONSTRAINT FK_SenderLine_DeletedByApiKey FOREIGN KEY (DeletedByApiKeyId) REFERENCES dbo.ApiKey (Id),
    CONSTRAINT CK_SenderLine_DeleteAudit CHECK
        ((DeletedAtUtc IS NULL AND DeletedByApiKeyId IS NULL) OR
         (DeletedAtUtc IS NOT NULL AND DeletedByApiKeyId IS NOT NULL));
GO

ALTER TABLE dbo.MessageType ADD
    CONSTRAINT FK_MessageType_DeletedByApiKey FOREIGN KEY (DeletedByApiKeyId) REFERENCES dbo.ApiKey (Id),
    CONSTRAINT CK_MessageType_DeleteAudit CHECK
        ((DeletedAtUtc IS NULL AND DeletedByApiKeyId IS NULL) OR
         (DeletedAtUtc IS NOT NULL AND DeletedByApiKeyId IS NOT NULL));
GO

ALTER TABLE dbo.GeoSection ADD
    CONSTRAINT FK_GeoSection_DeletedByApiKey FOREIGN KEY (DeletedByApiKeyId) REFERENCES dbo.ApiKey (Id),
    CONSTRAINT CK_GeoSection_DeleteAudit CHECK
        ((DeletedAtUtc IS NULL AND DeletedByApiKeyId IS NULL) OR
         (DeletedAtUtc IS NOT NULL AND DeletedByApiKeyId IS NOT NULL));
GO
