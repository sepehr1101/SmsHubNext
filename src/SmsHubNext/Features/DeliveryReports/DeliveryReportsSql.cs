namespace SmsHubNext.Features.DeliveryReports;

internal static class DeliveryReportsSql
{
    // Fetch the message's partition key (and prove it exists) before recording a report.
    public const string GetMessagePartition =
        "SELECT SubmitDateJalali FROM dbo.Message WHERE Id = @MessageId;";

    public const string InsertReport =
        """
        INSERT INTO dbo.DeliveryReport (SubmitDateJalali, MessageId, NormalizedStatus, RawStatusCode, ReceivedAtUtc)
        OUTPUT INSERTED.Id
        VALUES (@SubmitDateJalali, @MessageId, @NormalizedStatus, @RawStatusCode, @ReceivedAtUtc);
        """;

    // Update the denormalized read model in place (README §4.10/§8.5). DeliveredAtUtc is
    // stamped only when the message becomes Delivered, and is never cleared by a later report.
    public const string UpdateMessageStatus =
        """
        UPDATE dbo.Message
        SET DeliveryStatus = @DeliveryStatus,
            DeliveredAtUtc = CASE WHEN @DeliveryStatus = @DeliveredValue THEN @ReceivedAtUtc ELSE DeliveredAtUtc END
        WHERE Id = @MessageId;
        """;

    public const string ListByMessage =
        """
        SELECT Id, SubmitDateJalali, MessageId, NormalizedStatus, RawStatusCode, ReceivedAtUtc
        FROM dbo.DeliveryReport
        WHERE MessageId = @MessageId
        ORDER BY ReceivedAtUtc DESC, Id DESC;
        """;
}
