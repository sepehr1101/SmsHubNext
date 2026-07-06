-- GeoSection: one self-referencing geographic hierarchy (README §4.7).

CREATE TABLE dbo.GeoSection
(
    Id                 INT           IDENTITY(1,1) NOT NULL,
    ParentGeoSectionId INT           NULL,
    SectionType        TINYINT       NOT NULL,  -- 1 = Province, 2 = City, 3 = Zone
    Name               NVARCHAR(100) NOT NULL,
    Code               VARCHAR(20)   NOT NULL,
    Path               VARCHAR(900)  NOT NULL,  -- materialized ancestor path, e.g. /1/3/
    IsActive           BIT           NOT NULL CONSTRAINT DF_GeoSection_IsActive DEFAULT (1),
    CONSTRAINT PK_GeoSection PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_GeoSection_Parent FOREIGN KEY (ParentGeoSectionId) REFERENCES dbo.GeoSection (Id)
);
GO

CREATE NONCLUSTERED INDEX IX_GeoSection_Code ON dbo.GeoSection (Code);
GO

CREATE NONCLUSTERED INDEX IX_GeoSection_Path ON dbo.GeoSection (Path);
GO
