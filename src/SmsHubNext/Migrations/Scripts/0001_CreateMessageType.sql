-- MessageType: the single classification axis (delivery class + business purpose).
-- README §4.6. User-configured reference data, TINYINT key.

CREATE TABLE dbo.MessageType
(
    Id   TINYINT      NOT NULL,
    Name NVARCHAR(80) NOT NULL,
    Code VARCHAR(50)  NOT NULL,
    CONSTRAINT PK_MessageType PRIMARY KEY CLUSTERED (Id)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX UX_MessageType_Code ON dbo.MessageType (Code);
GO
