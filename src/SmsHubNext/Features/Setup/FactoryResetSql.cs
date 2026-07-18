namespace SmsHubNext.Features.Setup;

internal static class FactoryResetSql
{
    public const string MessagesExist =
        """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT TOP (1) 1
            FROM dbo.Message WITH (TABLOCKX, HOLDLOCK)
        ) THEN 1 ELSE 0 END AS bit);
        """;

    public const string ResetDatabase =
        """
        -- Clear outbound/inbound operational data first.
        DELETE FROM dbo.DeliveryReportPoll;
        DELETE FROM dbo.DeliveryReport;
        DELETE FROM dbo.MessageBody;
        DELETE FROM dbo.Message;
        DELETE FROM dbo.MessageBatchEvent;
        DELETE FROM dbo.BalanceTransaction;
        DELETE FROM dbo.MessageBatch;
        DELETE FROM dbo.InboundMessage;

        -- Break attribution cycles before removing API keys and reference data.
        UPDATE dbo.Customer SET DeletedAtUtc = NULL, DeletedByApiKeyId = NULL;
        UPDATE dbo.Provider SET DeletedAtUtc = NULL, DeletedByApiKeyId = NULL;
        UPDATE dbo.SenderLine SET DeletedAtUtc = NULL, DeletedByApiKeyId = NULL;
        UPDATE dbo.MessageType SET DeletedAtUtc = NULL, DeletedByApiKeyId = NULL;
        UPDATE dbo.GeoSection SET DeletedAtUtc = NULL, DeletedByApiKeyId = NULL;
        UPDATE dbo.Tariff SET DeletedAtUtc = NULL, DeletedByApiKeyId = NULL;
        UPDATE dbo.ProviderAccount SET DeletedAtUtc = NULL, DeletedByApiKeyId = NULL;
        UPDATE dbo.ApiKeyIpRestriction SET DeletedAtUtc = NULL, DeletedByApiKeyId = NULL;
        UPDATE dbo.ApiKey SET RevokedByApiKeyId = NULL;

        -- Clear installation-specific business configuration in FK-safe order.
        DELETE FROM dbo.ApiKeyIpRestriction;
        DELETE FROM dbo.CustomerBalance;
        DELETE FROM dbo.TariffRate;
        DELETE FROM dbo.Tariff;
        UPDATE dbo.SenderLine SET ProviderAccountId = NULL;
        DELETE FROM dbo.SenderLine;
        DELETE FROM dbo.ProviderAccount;
        DELETE FROM dbo.GeoSection;
        DELETE FROM dbo.ApiKey;
        DELETE FROM dbo.Customer;
        DELETE FROM dbo.MessageType;
        DELETE FROM dbo.Provider;

        -- A fresh installation has no business/reference rows; the wizard recreates them.
        DBCC CHECKIDENT ('dbo.Provider', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.SenderLine', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.Customer', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.ApiKey', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.GeoSection', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.Tariff', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.TariffRate', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.BalanceTransaction', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.ApiKeyIpRestriction', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.MessageBatch', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.Message', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.DeliveryReport', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.InboundMessage', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.MessageBatchEvent', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('dbo.ProviderAccount', RESEED, 0) WITH NO_INFOMSGS;
        """;
}
