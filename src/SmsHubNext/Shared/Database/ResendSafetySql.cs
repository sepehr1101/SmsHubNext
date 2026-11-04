namespace SmsHubNext.Shared.Database;

internal static class ResendSafetySql
{
    // Alias m is a source Message. Never infer definite non-delivery from polling expiry or a failed query.
    public const string Eligibility = """
        m.DeliveryStatus <> 2
        AND NOT EXISTS (SELECT 1 FROM dbo.DeliveryReport dr WHERE dr.SubmitDateJalali=m.SubmitDateJalali AND dr.MessageId=m.Id AND dr.NormalizedStatus=1)
        AND (
            (m.Status=4 AND m.ProviderMessageId IS NULL
             AND NOT EXISTS (SELECT 1 FROM dbo.DispatchAcceptance a WHERE a.Id=m.Id))
            OR (m.Status IN (2,3) AND m.DeliveryStatus=3 AND m.ProviderMessageId IS NOT NULL
                AND (SELECT TOP (1) dr.NormalizedStatus FROM dbo.DeliveryReport dr
                     WHERE dr.SubmitDateJalali=m.SubmitDateJalali AND dr.MessageId=m.Id ORDER BY dr.ReceivedAtUtc DESC, dr.Id DESC) IN (2,4))
        )
        """;

    // Match the durable request key as well as the source ID. This works even if acceptance committed
    // but a crash prevented the manifest's NewMessageId from being filled in.
    public const string UnsafeBeforeDispatch = """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1 FROM dbo.Message child
            JOIN dbo.DailyResendItem i ON i.SourceMessageId=TRY_CONVERT(BIGINT,PARSENAME(REPLACE(child.ClientCorrelatedId,':','.'),2))
              AND child.ClientCorrelatedId=CONCAT('resend:',i.SourceMessageId,':',LOWER(REPLACE(CONVERT(VARCHAR(36),i.RequestId),'-','')))
            LEFT JOIN dbo.Message m ON m.Id=i.SourceMessageId
            WHERE child.Id IN @Ids AND CASE WHEN
        """ + "\n" + Eligibility + "\n" + """
            THEN 0 ELSE 1 END=1
        ) THEN 1 ELSE 0 END AS bit);
        """;
}
