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

    // --- DLR polling queue (Phase 2) -------------------------------------------------------

    // Atomically claim the due poll rows and lease them forward (NextPollAtUtc) so a slow cycle
    // or a second worker doesn't re-claim them. READPAST/UPDLOCK let workers skip each other's
    // in-flight rows. Terminal rows are deleted after processing; in-flight rows keep the lease.
    public const string ClaimDuePolls =
        """
        ;WITH due AS (
            SELECT TOP (@BatchSize) MessageId
            FROM dbo.DeliveryReportPoll WITH (READPAST, UPDLOCK, ROWLOCK)
            WHERE NextPollAtUtc <= @Now
            ORDER BY NextPollAtUtc
        )
        UPDATE p
        SET NextPollAtUtc = @NextPollAtUtc, Attempts = p.Attempts + 1
        OUTPUT INSERTED.MessageId, INSERTED.SubmitDateJalali, INSERTED.ProviderId,
               INSERTED.ProviderMessageId, INSERTED.DispatchedAtUtc, INSERTED.Attempts
        FROM dbo.DeliveryReportPoll p
        INNER JOIN due ON due.MessageId = p.MessageId;
        """;

    // Apply a terminal delivery outcome, idempotently: project it onto the Message read model
    // (guarded on Pending so only the first terminal wins and stamps DeliveredAtUtc), append the
    // immutable DeliveryReport for that first transition only, then dequeue. Run in one transaction.
    public const string ApplyTerminalReport =
        """
        UPDATE dbo.Message
        SET DeliveryStatus = @DeliveryStatus,
            DeliveredAtUtc = CASE WHEN @DeliveryStatus = @DeliveredValue THEN @ReceivedAtUtc ELSE DeliveredAtUtc END
        WHERE Id = @MessageId AND DeliveryStatus = @PendingValue;

        IF @@ROWCOUNT = 1
            INSERT INTO dbo.DeliveryReport (SubmitDateJalali, MessageId, NormalizedStatus, RawStatusCode, ReceivedAtUtc)
            VALUES (@SubmitDateJalali, @MessageId, @NormalizedStatus, @RawStatusCode, @ReceivedAtUtc);

        DELETE FROM dbo.DeliveryReportPoll WHERE MessageId = @MessageId;
        """;
}
