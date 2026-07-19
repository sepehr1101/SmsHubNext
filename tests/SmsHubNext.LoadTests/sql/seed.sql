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

DECLARE @ProviderId TINYINT;
DECLARE @CustomerId SMALLINT;
DECLARE @ProviderAccountId INT;
DECLARE @TariffId INT;
DECLARE @InitialBalance DECIMAL(19,4) = 1000000000000.0000;
DECLARE @RawApiKey VARCHAR(100) = 'smshub-load-test-key-2026';

SELECT @ProviderId = Id
FROM dbo.Provider
WHERE Code = 'magfa';

IF @ProviderId IS NULL
BEGIN
    INSERT INTO dbo.Provider (Name, Code, BaseUrl, IsActive)
    VALUES (N'Magfa load-test provider', 'magfa', 'http://loopback.invalid', 1);

    SET @ProviderId = CONVERT(TINYINT, SCOPE_IDENTITY());
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MessageType WHERE Id = 250)
BEGIN
    INSERT INTO dbo.MessageType (Id, Name, Code)
    VALUES (250, N'Load Test', 'load-test');
END;

SELECT @CustomerId = Id
FROM dbo.Customer
WHERE Code = 'load-test-customer';

IF @CustomerId IS NULL
BEGIN
    INSERT INTO dbo.Customer (Name, Code, IsActive)
    VALUES (N'Load Test Customer', 'load-test-customer', 1);

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
        N'Load Test Key',
        'smshub-load',
        HASHBYTES('SHA2_256', CONVERT(VARBINARY(MAX), @RawApiKey)),
        1
    );
END;

SELECT @ProviderAccountId = Id
FROM dbo.ProviderAccount
WHERE ProviderId = @ProviderId
  AND DisplayName = N'Load Test Provider Account';

IF @ProviderAccountId IS NULL
BEGIN
    INSERT INTO dbo.ProviderAccount
        (ProviderId, DisplayName, AuthType, SettingsJson, SecretEncrypted, IsActive)
    VALUES
        (@ProviderId, N'Load Test Provider Account', 'UsernamePasswordDomain',
         N'{"username":"load","domain":"load"}', 0x01, 1);

    SET @ProviderAccountId = CONVERT(INT, SCOPE_IDENTITY());
END;

IF NOT EXISTS (SELECT 1 FROM dbo.SenderLine WHERE LineNumber = '300099999999')
BEGIN
    INSERT INTO dbo.SenderLine
        (ProviderId, LineNumber, IsSharedLine, IsActive, CustomerId, ProviderAccountId)
    VALUES
        (@ProviderId, '300099999999', 1, 1, NULL, @ProviderAccountId);
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
        (@CustomerId, 1, @InitialBalance, @InitialBalance, 'load-test-seed');
END;

COMMIT TRANSACTION;

SELECT
    @ProviderId AS ProviderId,
    @CustomerId AS CustomerId,
    @ProviderAccountId AS ProviderAccountId,
    @TariffId AS TariffId;
