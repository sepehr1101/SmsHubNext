-- Customer: the tenancy / isolation / reporting boundary (README §4.1).

CREATE TABLE dbo.Customer
(
    Id           SMALLINT      IDENTITY(1,1) NOT NULL,
    Name         NVARCHAR(200) NOT NULL,
    Code         VARCHAR(50)   NOT NULL,
    IsActive     BIT           NOT NULL CONSTRAINT DF_Customer_IsActive DEFAULT (1),
    CreatedAtUtc DATETIME2(3)  NOT NULL CONSTRAINT DF_Customer_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Customer PRIMARY KEY CLUSTERED (Id)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX UX_Customer_Code ON dbo.Customer (Code);
GO
