SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRANSACTION;

DECLARE @ProviderId TINYINT = 3;
DECLARE @CustomerId SMALLINT;
DECLARE @TariffId INT;
DECLARE @InitialBalance DECIMAL(19,4) = 1000000000000.0000;
DECLARE @RawApiKey VARCHAR(100) = 'smshub-smsir-sandbox-customer-key-2026';

IF NOT EXISTS (SELECT 1 FROM dbo.Provider WHERE Code = 'smsir')
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.Provider WHERE Id = @ProviderId)
        THROW 51100, 'Cannot reserve Provider.Id 3 for the sms.ir sandbox test.', 1;

    SET IDENTITY_INSERT dbo.Provider ON;
    INSERT INTO dbo.Provider (Id, Name, Code, BaseUrl, IsActive)
    VALUES (@ProviderId, N'sms.ir Sandbox', 'smsir', 'https://api.sms.ir', 1);
    SET IDENTITY_INSERT dbo.Provider OFF;
END;
ELSE
BEGIN
    SELECT @ProviderId = Id FROM dbo.Provider WHERE Code = 'smsir';
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MessageType WHERE Id = 250)
BEGIN
    INSERT INTO dbo.MessageType (Id, Name, Code)
    VALUES (250, N'sms.ir Sandbox Load Test', 'smsir-sandbox-load');
END;

SELECT @CustomerId = Id
FROM dbo.Customer
WHERE Code = 'smsir-sandbox-customer';

IF @CustomerId IS NULL
BEGIN
    INSERT INTO dbo.Customer (Name, Code, IsActive)
    VALUES (N'sms.ir Sandbox Customer', 'smsir-sandbox-customer', 1);

    SET @CustomerId = CONVERT(SMALLINT, SCOPE_IDENTITY());
END;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ApiKey
    WHERE KeyHash = HASHBYTES('SHA2_256', CONVERT(VARBINARY(MAX), @RawApiKey))
)
BEGIN
    INSERT INTO dbo.ApiKey (CustomerId, Name, KeyPrefix, KeyHash, IsActive)
    VALUES (
        @CustomerId,
        N'sms.ir Sandbox Load Key',
        'smshub-smsir',
        HASHBYTES('SHA2_256', CONVERT(VARBINARY(MAX), @RawApiKey)),
        1
    );
END;

SELECT TOP (1) @TariffId = Id
FROM dbo.Tariff
WHERE ProviderId = @ProviderId
  AND MessageTypeId IS NULL
  AND Encoding = 0
  AND EffectiveToUtc IS NULL
  AND IsActive = 1
  AND DeletedAtUtc IS NULL
ORDER BY Id;

IF @TariffId IS NULL
BEGIN
    INSERT INTO dbo.Tariff
        (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, EffectiveToUtc, Currency, IsActive)
    VALUES
        (@ProviderId, NULL, 0, '2025-01-01T00:00:00', NULL, 'IRR', 1);

    SET @TariffId = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.TariffRate (TariffId, MinChars, MaxChars, PricePerSegment)
    VALUES (@TariffId, 1, NULL, 1000.0000);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.CustomerBalance WHERE CustomerId = @CustomerId)
BEGIN
    INSERT INTO dbo.CustomerBalance (CustomerId, Balance)
    VALUES (@CustomerId, @InitialBalance);

    INSERT INTO dbo.BalanceTransaction
        (CustomerId, Type, Amount, BalanceAfter, Reference)
    VALUES
        (@CustomerId, 1, @InitialBalance, @InitialBalance, 'smsir-sandbox-seed');
END;

COMMIT TRANSACTION;

SELECT
    @ProviderId AS ProviderId,
    @CustomerId AS CustomerId,
    @TariffId AS TariffId;
