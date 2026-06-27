-- Tariff (versioned header) + TariffRate (per-segment price banding). README §4.8/§4.9.

CREATE TABLE dbo.Tariff
(
    Id               INT          IDENTITY(1,1) NOT NULL,
    ProviderId       TINYINT      NOT NULL,
    MessageTypeId    TINYINT      NULL,           -- null = applies to all message types
    Encoding         TINYINT      NOT NULL,       -- 0 = GSM7, 1 = UCS2 (SmsEncoding)
    EffectiveFromUtc DATETIME2(3) NOT NULL,
    EffectiveToUtc   DATETIME2(3) NULL,           -- null = open-ended
    Currency         CHAR(3)      NOT NULL CONSTRAINT DF_Tariff_Currency DEFAULT ('IRR'),
    IsActive         BIT          NOT NULL CONSTRAINT DF_Tariff_IsActive DEFAULT (1),
    CONSTRAINT PK_Tariff PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Tariff_Provider FOREIGN KEY (ProviderId) REFERENCES dbo.Provider (Id),
    CONSTRAINT FK_Tariff_MessageType FOREIGN KEY (MessageTypeId) REFERENCES dbo.MessageType (Id)
);
GO

CREATE NONCLUSTERED INDEX IX_Tariff_Lookup
    ON dbo.Tariff (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc);
GO

CREATE TABLE dbo.TariffRate
(
    Id              INT           IDENTITY(1,1) NOT NULL,
    TariffId        INT           NOT NULL,
    MinChars        SMALLINT      NOT NULL,
    MaxChars        SMALLINT      NULL,           -- null = unbounded
    PricePerSegment DECIMAL(19,4) NOT NULL,
    CONSTRAINT PK_TariffRate PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_TariffRate_Tariff FOREIGN KEY (TariffId) REFERENCES dbo.Tariff (Id)
);
GO

-- Seed a GSM-7 tariff for Magfa (all message types) with a flat per-segment price.
SET IDENTITY_INSERT dbo.Tariff ON;
INSERT INTO dbo.Tariff (Id, ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, EffectiveToUtc, Currency, IsActive) VALUES
    (1, 1, NULL, 0, '2025-01-01T00:00:00', NULL, 'IRR', 1);
SET IDENTITY_INSERT dbo.Tariff OFF;
GO

INSERT INTO dbo.TariffRate (TariffId, MinChars, MaxChars, PricePerSegment) VALUES
    (1, 0, NULL, 1000.0000);
GO
