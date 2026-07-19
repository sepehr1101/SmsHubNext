-- Provider: SMS providers. Endpoints live here; credentials do NOT (README §4.4).

CREATE TABLE dbo.Provider
(
    Id              TINYINT       IDENTITY(1,1) NOT NULL,
    Name            NVARCHAR(100) NOT NULL,
    Code            VARCHAR(50)   NOT NULL,
    BaseUrl         VARCHAR(300)  NOT NULL,
    FallbackBaseUrl VARCHAR(300)  NULL,
    IsActive        BIT           NOT NULL CONSTRAINT DF_Provider_IsActive DEFAULT (1),
    CONSTRAINT PK_Provider PRIMARY KEY CLUSTERED (Id)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX UX_Provider_Code ON dbo.Provider (Code);
GO
