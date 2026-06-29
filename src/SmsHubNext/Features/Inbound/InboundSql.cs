namespace SmsHubNext.Features.Inbound;

internal static class InboundSql
{
    // Resolve a provider's surrogate id from its stable code (the active ISmsProvider.Name).
    public const string ResolveProviderId =
        "SELECT Id FROM dbo.Provider WHERE Code = @Code;";

    // Newest-first inbox, optionally filtered to one receiving number.
    public const string ListRecent =
        """
        SELECT TOP (@Take)
            Id, ProviderId, SenderNumber, RecipientNumber, Body, ProviderTimestamp, ReceivedAtUtc
        FROM dbo.InboundMessage
        WHERE (@RecipientNumber IS NULL OR RecipientNumber = @RecipientNumber)
        ORDER BY ReceivedAtUtc DESC, Id DESC;
        """;

    // The insert is a bulk copy (SqlBulkCopy); the column list lives in InboundPoller's DataTable.
}
