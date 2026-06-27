-- MessageType: the single classification axis (delivery class + business purpose).
-- README §4.6. Seeded reference data, TINYINT key.

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

INSERT INTO dbo.MessageType (Id, Name, Code) VALUES
    (1, N'OTP',           'otp'),
    (2, N'Transactional', 'transactional'),
    (3, N'Bulk',          'bulk'),
    (4, N'Water Bill',    'water-bill');
GO
