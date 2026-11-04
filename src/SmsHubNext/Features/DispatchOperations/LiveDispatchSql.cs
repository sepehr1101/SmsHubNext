namespace SmsHubNext.Features.DispatchOperations;

internal static class LiveDispatchSql
{
    public const string Summary = """
        SELECT COUNT_BIG(CASE WHEN Status = 1 THEN 1 END) Queued,
               COUNT_BIG(CASE WHEN Status = 6 THEN 1 END) Unconfirmed,
               COUNT_BIG(CASE WHEN Status IN (2,3) THEN 1 END) Accepted,
               COUNT_BIG(CASE WHEN Status = 3 THEN 1 END) Sent,
               COUNT_BIG(CASE WHEN Status = 4 THEN 1 END) Rejected,
               COUNT_BIG(CASE WHEN DeliveryStatus = 2 THEN 1 END) Delivered,
               COUNT_BIG(CASE WHEN DeliveryStatus IN (3,4) THEN 1 END) DeliveryFailed
        FROM dbo.Message
        WHERE SubmitDateJalali >= @FromJalali AND SubmitDateJalali <= @ToJalali AND SubmittedAtUtc >= @From AND SubmittedAtUtc < @Until;
        """;
    public const string Chart = """
        SELECT DATEADD(second, DATEDIFF(second, @Start, AcceptedAtUtc) / 10 * 10, @Start) StartUtc, COUNT_BIG(*) Count
        FROM dbo.DispatchAcceptance
        WHERE AcceptedAtUtc >= @Start AND AcceptedAtUtc < @End
        GROUP BY DATEDIFF(second, @Start, AcceptedAtUtc) / 10;
        """;
    public const string Messages = """
        WITH Selected AS (
            SELECT TOP (@Take) m.* FROM dbo.Message m
            WHERE m.SubmitDateJalali >= @FromJalali AND m.SubmitDateJalali <= @ToJalali AND m.SubmittedAtUtc >= @From AND m.SubmittedAtUtc < @Until
              AND (@Status IS NULL OR m.Status = @Status)
              AND (@Provider IS NULL OR m.ProviderId = @Provider)
              AND (@Batch IS NULL OR m.MessageBatchId = @Batch)
              AND (@HasSearch = 0 OR m.MobileNumber = @SearchMobile OR m.Id = @SearchId)
              AND (@BeforeId IS NULL OR m.SubmitDateJalali < @BeforeDate
                   OR (m.SubmitDateJalali = @BeforeDate AND m.Id < @BeforeId))
            ORDER BY m.SubmitDateJalali DESC, m.Id DESC
        )
        SELECT m.Id, m.SubmitDateJalali, m.MessageBatchId, m.MobileNumber, s.LineNumber SenderLine, p.Name ProviderName,
               m.Status, m.DeliveryStatus, m.SubmittedAtUtc, a.AcceptedAtUtc, b.DispatchAttemptCount, body.Body,
               e.EventTimeUtc ErrorAtUtc, e.ProviderResultCode ErrorCode, e.EventType ErrorEvent
        FROM Selected m
        JOIN dbo.Provider p ON p.Id = m.ProviderId
        JOIN dbo.SenderLine s ON s.Id = m.SenderLineId
        JOIN dbo.MessageBatch b ON b.Id = m.MessageBatchId
        LEFT JOIN dbo.DispatchAcceptance a ON a.Id = m.Id
        LEFT JOIN dbo.MessageBody body ON body.Id = m.Id
        OUTER APPLY (
            SELECT TOP (1) EventTimeUtc, ProviderResultCode, EventType
            FROM dbo.MessageBatchEvent
            WHERE MessageBatchId = m.MessageBatchId AND EventType IN (5,7,10)
            ORDER BY EventTimeUtc DESC, Id DESC
        ) e
        ORDER BY m.SubmitDateJalali DESC, m.Id DESC OPTION (RECOMPILE);
        """;
}
