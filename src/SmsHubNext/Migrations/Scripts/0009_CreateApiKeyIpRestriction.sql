-- ApiKeyIpRestriction: optional per-key CIDR allow-list (README §4.3).

CREATE TABLE dbo.ApiKeyIpRestriction
(
    Id          INT           IDENTITY(1,1) NOT NULL,
    ApiKeyId    INT           NOT NULL,
    Cidr        VARCHAR(43)   NOT NULL,
    Description NVARCHAR(100) NULL,
    CONSTRAINT PK_ApiKeyIpRestriction PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ApiKeyIpRestriction_ApiKey FOREIGN KEY (ApiKeyId) REFERENCES dbo.ApiKey (Id)
);
GO

CREATE NONCLUSTERED INDEX IX_ApiKeyIpRestriction_ApiKeyId ON dbo.ApiKeyIpRestriction (ApiKeyId);
GO
