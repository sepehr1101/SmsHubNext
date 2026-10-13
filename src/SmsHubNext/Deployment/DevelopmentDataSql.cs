namespace SmsHubNext.Deployment;

internal static class DevelopmentDataSql
{
    public const string Seed =
        """
        SET NOCOUNT ON;

        DECLARE @SystemCustomerId SMALLINT;
        SELECT @SystemCustomerId = Id FROM dbo.Customer WHERE IsSystem = 1;
        IF @SystemCustomerId IS NULL
        BEGIN
            INSERT INTO dbo.Customer (Name, Code, IsActive, IsSystem)
            VALUES (N'SmsHubNext System', 'smshubnext-system', 1, 1);
            SET @SystemCustomerId = CONVERT(SMALLINT, SCOPE_IDENTITY());
        END;

        DECLARE @CustomerId SMALLINT;
        SELECT @CustomerId = Id FROM dbo.Customer WHERE Code = 'afjeh-water';
        IF @CustomerId IS NULL
        BEGIN
            INSERT INTO dbo.Customer (Name, Code, IsActive, IsSystem)
            VALUES (N'شورای آب روستای افجه', 'afjeh-water', 1, 0);
            SET @CustomerId = CONVERT(SMALLINT, SCOPE_IDENTITY());
        END
        ELSE
            UPDATE dbo.Customer SET Name = N'شورای آب روستای افجه', IsActive = 1, DeletedAtUtc = NULL, DeletedByApiKeyId = NULL WHERE Id = @CustomerId;

        IF NOT EXISTS (SELECT 1 FROM dbo.ApiKey WHERE KeyHash = @AdministratorKeyHash)
            INSERT INTO dbo.ApiKey (CustomerId, Name, KeyPrefix, KeyHash, AccessLevel, IsActive)
            VALUES (@SystemCustomerId, N'کلید مدیریت توسعه', 'shn_dev_admi', @AdministratorKeyHash, 1, 1);

        IF NOT EXISTS (SELECT 1 FROM dbo.ApiKey WHERE KeyHash = @CustomerKeyHash)
            INSERT INTO dbo.ApiKey (CustomerId, Name, KeyPrefix, KeyHash, AccessLevel, IsActive)
            VALUES (@CustomerId, N'کلید ارسال آزمایشی', 'shn_dev_cust', @CustomerKeyHash, 0, 1);

        DECLARE @CustomerApiKeyId INT = (SELECT Id FROM dbo.ApiKey WHERE KeyHash = @CustomerKeyHash);

        DECLARE @ProviderId TINYINT;
        SELECT @ProviderId = Id FROM dbo.Provider WHERE Code = 'magfa';
        IF @ProviderId IS NULL
        BEGIN
            INSERT INTO dbo.Provider (Name, Code, BaseUrl, IsActive)
            VALUES (N'مگفا', 'magfa', 'https://sms.magfa.com', 1);
            SET @ProviderId = CONVERT(TINYINT, SCOPE_IDENTITY());
        END
        ELSE
            UPDATE dbo.Provider SET Name = N'مگفا', BaseUrl = 'https://sms.magfa.com', IsActive = 1, DeletedAtUtc = NULL, DeletedByApiKeyId = NULL WHERE Id = @ProviderId;

        DECLARE @ProviderAccountId INT;
        DECLARE @Provider4040Id TINYINT;
        SELECT @Provider4040Id = Id FROM dbo.Provider WHERE Code = '4040';
        IF @Provider4040Id IS NULL
        BEGIN
            INSERT INTO dbo.Provider (Name, Code, BaseUrl, IsActive)
            VALUES (N'4040', '4040', 'https://esb.abfaisfahan.com:8243', 1);
            SET @Provider4040Id = CONVERT(TINYINT, SCOPE_IDENTITY());
        END
        ELSE
            UPDATE dbo.Provider SET IsActive = 1
            WHERE Id = @Provider4040Id AND DeletedAtUtc IS NULL;

        IF NOT EXISTS (SELECT 1 FROM dbo.SenderLine WHERE ProviderId = @Provider4040Id AND LineNumber = '40401403')
            INSERT INTO dbo.SenderLine (ProviderId, LineNumber, IsSharedLine, CustomerId, ProviderAccountId, IsActive, UseEsb)
            VALUES (@Provider4040Id, '40401403', 0, @CustomerId, NULL, 1, 1);
        ELSE
            UPDATE dbo.SenderLine
            SET UseEsb = 1, IsActive = 1, IsSharedLine = 0, CustomerId = @CustomerId, ProviderAccountId = NULL
            WHERE ProviderId = @Provider4040Id AND LineNumber = '40401403' AND DeletedAtUtc IS NULL;

        -- Development-only prices, not the provider's commercial tariff. Global fallback
        -- covers every seeded message type and both Latin/Persian test bodies.
        DECLARE @Encoding4040 TINYINT = 0;
        WHILE @Encoding4040 <= 1
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM dbo.Tariff
                WHERE ProviderId = @Provider4040Id AND MessageTypeId IS NULL AND Encoding = @Encoding4040)
            BEGIN
                INSERT INTO dbo.Tariff (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, Currency, IsActive)
                VALUES (@Provider4040Id, NULL, @Encoding4040, '2026-01-01', 'IRR', 1);
                DECLARE @Tariff4040Id INT = CONVERT(INT, SCOPE_IDENTITY());
                INSERT INTO dbo.TariffRate (TariffId, MinChars, MaxChars, PricePerSegment)
                VALUES (@Tariff4040Id, 1, NULL, 1500);
            END;
            SET @Encoding4040 = @Encoding4040 + 1;
        END;

        SELECT @ProviderAccountId = pa.Id FROM dbo.ProviderAccount pa WHERE pa.ProviderId = @ProviderId AND pa.DisplayName = N'حساب آزمایشی مگفا';
        IF @ProviderAccountId IS NULL
        BEGIN
            INSERT INTO dbo.ProviderAccount (ProviderId, DisplayName, AuthType, SettingsJson, SecretEncrypted, IsActive)
            VALUES (@ProviderId, N'حساب آزمایشی مگفا', 'UsernamePasswordDomain', N'{"username":"development-user","domain":"magfa"}', @ProviderSecret, 1);
            SET @ProviderAccountId = CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
            UPDATE dbo.ProviderAccount
            SET AuthType = 'UsernamePasswordDomain',
                SettingsJson = N'{"username":"development-user","domain":"magfa"}',
                SecretEncrypted = @ProviderSecret,
                IsActive = 1,
                UpdatedAtUtc = SYSUTCDATETIME(),
                DeletedAtUtc = NULL,
                DeletedByApiKeyId = NULL
            WHERE Id = @ProviderAccountId;

        DECLARE @MessageTypeId TINYINT;
        SELECT @MessageTypeId = Id FROM dbo.MessageType WHERE Code = 'water_bill';
        IF @MessageTypeId IS NULL
        BEGIN
            SELECT @MessageTypeId = CONVERT(TINYINT, COALESCE(MAX(CONVERT(INT, Id)), 0) + 1) FROM dbo.MessageType;
            INSERT INTO dbo.MessageType (Id, Name, Code, IsActive) VALUES (@MessageTypeId, N'قبض آب', 'water_bill', 1);
        END;

        IF NOT EXISTS (SELECT 1 FROM dbo.MessageType WHERE Code = 'notification')
        BEGIN
            DECLARE @NotificationTypeId TINYINT;
            SELECT @NotificationTypeId = CONVERT(TINYINT, COALESCE(MAX(CONVERT(INT, Id)), 0) + 1) FROM dbo.MessageType;
            INSERT INTO dbo.MessageType (Id, Name, Code, IsActive) VALUES (@NotificationTypeId, N'اطلاع‌رسانی', 'notification', 1);
        END;

        DECLARE @ProvinceId INT;
        SELECT @ProvinceId = Id FROM dbo.GeoSection WHERE Code = 'tehran';
        IF @ProvinceId IS NULL
        BEGIN
            INSERT INTO dbo.GeoSection (ParentGeoSectionId, SectionType, Name, Code, Path) VALUES (NULL, 1, N'تهران', 'tehran', '');
            SET @ProvinceId = CONVERT(INT, SCOPE_IDENTITY());
            UPDATE dbo.GeoSection SET Path = CONCAT('/', @ProvinceId, '/') WHERE Id = @ProvinceId;
        END;

        DECLARE @CityId INT;
        SELECT @CityId = Id FROM dbo.GeoSection WHERE Code = 'shemiranat';
        IF @CityId IS NULL
        BEGIN
            INSERT INTO dbo.GeoSection (ParentGeoSectionId, SectionType, Name, Code, Path) VALUES (@ProvinceId, 2, N'شمیرانات', 'shemiranat', '');
            SET @CityId = CONVERT(INT, SCOPE_IDENTITY());
            UPDATE dbo.GeoSection SET Path = CONCAT('/', @ProvinceId, '/', @CityId, '/') WHERE Id = @CityId;
        END;

        DECLARE @ZoneId INT;
        SELECT @ZoneId = Id FROM dbo.GeoSection WHERE Code = 'afjeh';
        IF @ZoneId IS NULL
        BEGIN
            INSERT INTO dbo.GeoSection (ParentGeoSectionId, SectionType, Name, Code, Path) VALUES (@CityId, 3, N'افجه', 'afjeh', '');
            SET @ZoneId = CONVERT(INT, SCOPE_IDENTITY());
            UPDATE dbo.GeoSection SET Path = CONCAT('/', @ProvinceId, '/', @CityId, '/', @ZoneId, '/') WHERE Id = @ZoneId;
        END;

        DECLARE @SenderLineId SMALLINT;
        SELECT @SenderLineId = Id FROM dbo.SenderLine WHERE ProviderId = @ProviderId AND LineNumber = '3000123456';
        IF @SenderLineId IS NULL
        BEGIN
            INSERT INTO dbo.SenderLine (ProviderId, LineNumber, IsSharedLine, CustomerId, ProviderAccountId, IsActive)
            VALUES (@ProviderId, '3000123456', 0, @CustomerId, @ProviderAccountId, 1);
            SET @SenderLineId = CONVERT(SMALLINT, SCOPE_IDENTITY());
        END;

        DECLARE @TariffId INT;
        SELECT @TariffId = Id FROM dbo.Tariff WHERE ProviderId = @ProviderId AND MessageTypeId = @MessageTypeId AND Encoding = 1 AND EffectiveFromUtc = '2026-01-01';
        IF @TariffId IS NULL
        BEGIN
            INSERT INTO dbo.Tariff (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, Currency, IsActive)
            VALUES (@ProviderId, @MessageTypeId, 1, '2026-01-01', 'IRR', 1);
            SET @TariffId = CONVERT(INT, SCOPE_IDENTITY());
            INSERT INTO dbo.TariffRate (TariffId, MinChars, MaxChars, PricePerSegment) VALUES
                (@TariffId, 1, 70, 1500), (@TariffId, 71, 134, 1450), (@TariffId, 135, NULL, 1400);
        END;

        IF NOT EXISTS (SELECT 1 FROM dbo.CustomerBalance WHERE CustomerId = @CustomerId)
            INSERT INTO dbo.CustomerBalance (CustomerId, Balance) VALUES (@CustomerId, 49895000);

        IF NOT EXISTS (SELECT 1 FROM dbo.BalanceTransaction WHERE CustomerId = @CustomerId AND Reference = 'DEV-SEED-TOPUP')
            INSERT INTO dbo.BalanceTransaction (CustomerId, Type, Amount, BalanceAfter, Reference, CreatedAtUtc)
            VALUES (@CustomerId, 1, 50000000, 50000000, 'DEV-SEED-TOPUP', DATEADD(DAY, -2, SYSUTCDATETIME()));

        DECLARE @CompletedBatchId BIGINT;
        SELECT @CompletedBatchId = Id FROM dbo.MessageBatch WHERE CustomerId = @CustomerId AND ClientBatchId = 'dev-seed-completed';
        IF @CompletedBatchId IS NULL
        BEGIN
            INSERT INTO dbo.MessageBatch (SubmitDateJalali, ReceivedAtUtc, CustomerId, ApiKeyId, SenderLineId, ProviderId, ClientBatchId, MessageCount, SegmentCount, TotalCost, Status, DispatchStartedAtUtc, FinishedAtUtc, StatusChangedAtUtc, DispatchAttemptCount)
            VALUES ('1405/07/18', DATEADD(HOUR,-5,SYSUTCDATETIME()), @CustomerId, @CustomerApiKeyId, @SenderLineId, @ProviderId, 'dev-seed-completed', 2, 2, 40000, 3, DATEADD(HOUR,-5,SYSUTCDATETIME()), DATEADD(HOUR,-5,SYSUTCDATETIME()), DATEADD(HOUR,-5,SYSUTCDATETIME()), 1);
            SET @CompletedBatchId = SCOPE_IDENTITY();
            INSERT INTO dbo.MessageBatchEvent (MessageBatchId, EventTimeUtc, EventType, BatchStatus, Detail) VALUES
                (@CompletedBatchId, DATEADD(HOUR,-5,SYSUTCDATETIME()), 1, 1, N'دسته آزمایشی پذیرفته شد.'),
                (@CompletedBatchId, DATEADD(MINUTE,-299,SYSUTCDATETIME()), 2, 2, N'ارسال آزمایشی آغاز شد.'),
                (@CompletedBatchId, DATEADD(MINUTE,-298,SYSUTCDATETIME()), 8, 3, N'ارسال آزمایشی تکمیل شد.');
        END;

        DECLARE @HeldBatchId BIGINT;
        SELECT @HeldBatchId = Id FROM dbo.MessageBatch WHERE CustomerId = @CustomerId AND ClientBatchId = 'dev-seed-held';
        IF @HeldBatchId IS NULL
        BEGIN
            INSERT INTO dbo.MessageBatch (SubmitDateJalali, ReceivedAtUtc, CustomerId, ApiKeyId, SenderLineId, ProviderId, ClientBatchId, MessageCount, SegmentCount, TotalCost, Status, StatusReason, StatusChangedAtUtc, DispatchAttemptCount, NextDispatchAtUtc)
            VALUES ('1405/07/18', DATEADD(HOUR,-2,SYSUTCDATETIME()), @CustomerId, @CustomerApiKeyId, @SenderLineId, @ProviderId, 'dev-seed-held', 2, 2, 35000, 5, 1, DATEADD(HOUR,-2,SYSUTCDATETIME()), 1, DATEADD(MINUTE,10,SYSUTCDATETIME()));
            SET @HeldBatchId = SCOPE_IDENTITY();
            INSERT INTO dbo.MessageBatchEvent (MessageBatchId, EventTimeUtc, EventType, BatchStatus, BatchStatusReason, Detail)
            VALUES (@HeldBatchId, DATEADD(HOUR,-2,SYSUTCDATETIME()), 6, 5, 1, N'اعتبار آزمایشی ارائه‌دهنده کافی نیست.');
        END;

        DECLARE @FailedBatchId BIGINT;
        SELECT @FailedBatchId = Id FROM dbo.MessageBatch WHERE CustomerId = @CustomerId AND ClientBatchId = 'dev-seed-failed';
        IF @FailedBatchId IS NULL
        BEGIN
            INSERT INTO dbo.MessageBatch (SubmitDateJalali, ReceivedAtUtc, CustomerId, ApiKeyId, SenderLineId, ProviderId, ClientBatchId, MessageCount, SegmentCount, TotalCost, Status, StatusReason, FinishedAtUtc, StatusChangedAtUtc, DispatchAttemptCount)
            VALUES ('1405/07/18', DATEADD(HOUR,-1,SYSUTCDATETIME()), @CustomerId, @CustomerApiKeyId, @SenderLineId, @ProviderId, 'dev-seed-failed', 2, 2, 30000, 7, 3, DATEADD(MINUTE,-45,SYSUTCDATETIME()), DATEADD(MINUTE,-45,SYSUTCDATETIME()), 5);
            SET @FailedBatchId = SCOPE_IDENTITY();
            INSERT INTO dbo.MessageBatchEvent (MessageBatchId, EventTimeUtc, EventType, BatchStatus, BatchStatusReason, Detail)
            VALUES (@FailedBatchId, DATEADD(MINUTE,-45,SYSUTCDATETIME()), 10, 7, 3, N'حداکثر تلاش آزمایشی انجام شد.');
        END;

        IF NOT EXISTS (SELECT 1 FROM dbo.Message WHERE MessageBatchId = @CompletedBatchId)
        BEGIN
            INSERT INTO dbo.Message (SubmitDateJalali, MessageBatchId, SubmittedAtUtc, CustomerId, ProviderId, SenderLineId, MessageTypeId, GeoSectionId, MobileNumber, ClientCorrelatedId, Encoding, CharacterCount, SegmentCount, TariffId, UnitPrice, TotalCost, Status, DeliveryStatus, DeliveredAtUtc, ProviderMessageId)
            VALUES
                ('1405/07/18', @CompletedBatchId, DATEADD(HOUR,-5,SYSUTCDATETIME()), @CustomerId, @ProviderId, @SenderLineId, @MessageTypeId, @ZoneId, '09121234567', 'dev-msg-001', 1, 42, 1, @TariffId, 20000, 20000, 3, 2, DATEADD(HOUR,-4,SYSUTCDATETIME()), 'DEV-001'),
                ('1405/07/18', @CompletedBatchId, DATEADD(HOUR,-5,SYSUTCDATETIME()), @CustomerId, @ProviderId, @SenderLineId, @MessageTypeId, @ZoneId, '09121234568', 'dev-msg-002', 1, 45, 1, @TariffId, 20000, 20000, 3, 2, DATEADD(HOUR,-4,SYSUTCDATETIME()), 'DEV-002');
        END;

        IF NOT EXISTS (SELECT 1 FROM dbo.Message WHERE MessageBatchId = @HeldBatchId)
            INSERT INTO dbo.Message (SubmitDateJalali, MessageBatchId, CustomerId, ProviderId, SenderLineId, MessageTypeId, GeoSectionId, MobileNumber, ClientCorrelatedId, Encoding, CharacterCount, SegmentCount, TariffId, UnitPrice, TotalCost, Status, DeliveryStatus)
            VALUES ('1405/07/18', @HeldBatchId, @CustomerId, @ProviderId, @SenderLineId, @MessageTypeId, @ZoneId, '09121234569', 'dev-msg-003', 1, 38, 1, @TariffId, 17500, 17500, 1, 1), ('1405/07/18', @HeldBatchId, @CustomerId, @ProviderId, @SenderLineId, @MessageTypeId, @ZoneId, '09121234570', 'dev-msg-004', 1, 39, 1, @TariffId, 17500, 17500, 1, 1);

        IF NOT EXISTS (SELECT 1 FROM dbo.Message WHERE MessageBatchId = @FailedBatchId)
            INSERT INTO dbo.Message (SubmitDateJalali, MessageBatchId, CustomerId, ProviderId, SenderLineId, MessageTypeId, GeoSectionId, MobileNumber, ClientCorrelatedId, Encoding, CharacterCount, SegmentCount, TariffId, UnitPrice, TotalCost, Status, DeliveryStatus)
            VALUES ('1405/07/18', @FailedBatchId, @CustomerId, @ProviderId, @SenderLineId, @MessageTypeId, @ZoneId, '09121234571', 'dev-msg-005', 1, 35, 1, @TariffId, 15000, 15000, 4, 3), ('1405/07/18', @FailedBatchId, @CustomerId, @ProviderId, @SenderLineId, @MessageTypeId, @ZoneId, '09121234572', 'dev-msg-006', 1, 35, 1, @TariffId, 15000, 15000, 4, 3);

        INSERT INTO dbo.MessageBody (Id, Body)
        SELECT m.Id, CONCAT(N'پیام آزمایشی SmsHubNext — ', m.ClientCorrelatedId)
        FROM dbo.Message m
        LEFT JOIN dbo.MessageBody mb ON mb.Id = m.Id
        WHERE m.ClientCorrelatedId LIKE 'dev-msg-%' AND mb.Id IS NULL;

        INSERT INTO dbo.DeliveryReport (SubmitDateJalali, MessageId, NormalizedStatus, RawStatusCode, ReceivedAtUtc)
        SELECT m.SubmitDateJalali, m.Id, 1, 0, DATEADD(HOUR,-4,SYSUTCDATETIME())
        FROM dbo.Message m
        WHERE m.ClientCorrelatedId IN ('dev-msg-001', 'dev-msg-002')
          AND NOT EXISTS (SELECT 1 FROM dbo.DeliveryReport dr WHERE dr.MessageId = m.Id);

        IF NOT EXISTS (SELECT 1 FROM dbo.InboundMessage WHERE Body LIKE N'%DEV-SEED-INBOUND%')
            INSERT INTO dbo.InboundMessage (ProviderId, SenderNumber, RecipientNumber, Body, ProviderTimestamp, ReceivedAtUtc)
            VALUES
                (@ProviderId, '09121234567', '30001234', N'عدد کنتور ۱۲۳۴ است. DEV-SEED-INBOUND', '1405/07/18 09:20', DATEADD(MINUTE,-90,SYSUTCDATETIME())),
                (@ProviderId, '09121112233', '30001234', N'قبض پرداخت شد، سپاسگزارم. DEV-SEED-INBOUND', '1405/07/18 10:05', DATEADD(MINUTE,-45,SYSUTCDATETIME())),
                (@ProviderId, '09351234567', '30001234', N'برای دریافت راهنما عدد ۱ را ارسال کردم. DEV-SEED-INBOUND', '1405/07/18 10:30', DATEADD(MINUTE,-20,SYSUTCDATETIME()));

        IF NOT EXISTS (SELECT 1 FROM dbo.BalanceTransaction WHERE CustomerId = @CustomerId AND Reference = 'DEV-SEED-DEBIT-COMPLETED')
            INSERT INTO dbo.BalanceTransaction (CustomerId, Type, Amount, BalanceAfter, MessageBatchId, Reference, CreatedAtUtc) VALUES
                (@CustomerId, 2, -40000, 49960000, @CompletedBatchId, 'DEV-SEED-DEBIT-COMPLETED', DATEADD(HOUR,-5,SYSUTCDATETIME())),
                (@CustomerId, 2, -35000, 49925000, @HeldBatchId, 'DEV-SEED-DEBIT-HELD', DATEADD(HOUR,-2,SYSUTCDATETIME())),
                (@CustomerId, 2, -30000, 49895000, @FailedBatchId, 'DEV-SEED-DEBIT-FAILED', DATEADD(HOUR,-1,SYSUTCDATETIME()));
        """;
}
