-- SenderLine: sending lines belonging to a provider (README §4.5).

CREATE TABLE dbo.SenderLine
(
    Id           SMALLINT    IDENTITY(1,1) NOT NULL,
    ProviderId   TINYINT     NOT NULL,
    LineNumber   VARCHAR(20) NOT NULL,
    IsSharedLine BIT         NOT NULL,
    IsActive     BIT         NOT NULL CONSTRAINT DF_SenderLine_IsActive DEFAULT (1),
    CONSTRAINT PK_SenderLine PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_SenderLine_Provider FOREIGN KEY (ProviderId) REFERENCES dbo.Provider (Id)
);
GO

CREATE NONCLUSTERED INDEX IX_SenderLine_LineNumber ON dbo.SenderLine (LineNumber);
GO

-- Seed a few Magfa lines (ProviderId 1): shared (3000.../4040...) and dedicated (1000...).
SET IDENTITY_INSERT dbo.SenderLine ON;
INSERT INTO dbo.SenderLine (Id, ProviderId, LineNumber, IsSharedLine, IsActive) VALUES
    (1, 1, '30001234', 1, 1),
    (2, 1, '10001234', 0, 1),
    (3, 1, '40401234', 1, 1);
SET IDENTITY_INSERT dbo.SenderLine OFF;
GO
