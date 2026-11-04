namespace SmsHubNext.Features.Reports;

internal static class MessageSearchSql
{
    public const string Search =
        """
        WITH Matches AS
        (
            SELECT TOP (@Take) m.Id, m.MessageBatchId, m.SubmitDateJalali, m.SubmittedAtUtc,
                   m.MobileNumber, m.BillId, m.PayId, m.CustomerId, m.ProviderId, m.SenderLineId,
                   m.Status, m.DeliveryStatus, m.TotalCost
            FROM dbo.Message m
            WHERE m.SubmitDateJalali >= @FromJalali AND m.SubmitDateJalali <= @ToJalali
              AND m.SubmittedAtUtc >= @FromUtc AND m.SubmittedAtUtc < @UntilUtc
              AND (@CustomerId IS NULL OR m.CustomerId = @CustomerId)
              AND (@BillId IS NULL OR m.BillId = @BillId)
              AND (@PayId IS NULL OR m.PayId = @PayId)
              AND (@MobileNumber IS NULL OR m.MobileNumber = @MobileNumber)
              AND (@BeforeId IS NULL OR m.SubmitDateJalali < @BeforeDate
                   OR (m.SubmitDateJalali = @BeforeDate AND m.Id < @BeforeId))
            ORDER BY m.SubmitDateJalali DESC, m.Id DESC
        )
        SELECT m.Id, m.MessageBatchId, m.SubmitDateJalali, m.SubmittedAtUtc,
               m.MobileNumber, m.BillId, m.PayId, c.Name AS CustomerName, p.Name AS ProviderName,
               m.Status, m.DeliveryStatus, m.TotalCost, b.Body, s.LineNumber AS SenderLine
        FROM Matches m
        INNER JOIN dbo.Customer c ON c.Id = m.CustomerId
        INNER JOIN dbo.Provider p ON p.Id = m.ProviderId
        INNER JOIN dbo.SenderLine s ON s.Id = m.SenderLineId
        LEFT JOIN dbo.MessageBody b ON b.Id = m.Id
        ORDER BY m.SubmitDateJalali DESC, m.Id DESC
        OPTION (RECOMPILE);
        """;
}
