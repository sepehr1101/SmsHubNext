using SmsHubNext.Shared.Database;

namespace SmsHubNext.Features.Sending;

internal static class DailyResendSql
{
    // Expiry (including local polling-window expiry), unknown and mere query failures are excluded.
    // Any recorded acceptance/delivery contradicts a claimed pre-submission rejection.
    public const string Eligibility = ResendSafetySql.Eligibility;

    public const string Candidates = """
        WITH DayRows AS (
            SELECT m.*, COUNT_BIG(*) OVER (PARTITION BY MobileNumber) RecipientCount
            FROM dbo.Message m WHERE SubmitDateJalali=@Day AND Id<=@Cutoff
        )
        SELECT m.Id SourceMessageId, m.ProviderId, m.MessageTypeId, m.Encoding,
               CAST(m.CharacterCount AS INT) CharacterCount, CAST(m.SegmentCount AS INT) SegmentCount,
               CASE WHEN m.RecipientCount<>1 THEN 'sending.daily_ambiguous_recipient'
                    WHEN body.Id IS NULL OR LEN(body.Body)=0 THEN 'sending.resend_body_missing'
                    WHEN EXISTS (SELECT 1 FROM dbo.MessageBatch b WHERE b.CustomerId=m.CustomerId
                         AND b.ClientBatchId LIKE CONCAT('resend:',m.Id,':%')) THEN 'sending.daily_previously_resent'
                    ELSE NULL END SkipCode
        FROM DayRows m
        LEFT JOIN dbo.MessageBody body ON body.Id=m.Id
        WHERE (m.ClientCorrelatedId IS NULL OR m.ClientCorrelatedId NOT LIKE 'resend:%')
          AND NOT EXISTS (SELECT 1 FROM dbo.DailyResendItem i WHERE i.SourceMessageId=m.Id)
          AND
        """ + "\n" + Eligibility + " ORDER BY m.Id;";

    public const string Summary = """
        SELECT o.Id, o.SubmitDateJalali, o.State,
            COUNT_BIG(i.Id) Examined,
            COUNT_BIG(CASE WHEN i.EstimatedCost IS NOT NULL THEN 1 END) Eligible,
            COALESCE(SUM(i.EstimatedCost),0) EstimatedCost,
            COUNT_BIG(i.NewMessageId) Queued,
            COUNT_BIG(CASE WHEN m.Status IN (2,3) THEN 1 END) Submitted,
            COUNT_BIG(CASE WHEN m.Status=4 THEN 1 END) Failed,
            COUNT_BIG(CASE WHEN m.Status IN (1,5,6) THEN 1 END) Pending,
            COUNT_BIG(CASE WHEN i.State=2 THEN 1 END) Skipped,
            COUNT_BIG(CASE WHEN i.NewMessageId IS NOT NULL AND m.Id IS NULL THEN 1 END) MissingHistory,
            COALESCE(SUM(CASE WHEN i.State=0 THEN i.EstimatedCost ELSE 0 END),0) RemainingCost
        FROM dbo.DailyResendOperation o
        LEFT JOIN dbo.DailyResendItem i ON i.OperationId=o.Id
        LEFT JOIN dbo.Message m ON m.Id=i.NewMessageId
        WHERE o.Id=@Id
        GROUP BY o.Id,o.SubmitDateJalali,o.State;
        """;

    public const string NextOperation = """
        SELECT TOP(1) Id, SubmitDateJalali, CutoffMessageId, State, ConfirmedByApiKeyId
        FROM dbo.DailyResendOperation WHERE State IN (0,2) ORDER BY CreatedAtUtc;
        """;
    public const string Identity = """
        SELECT k.Id ApiKeyId,k.CustomerId,k.KeyPrefix,k.AccessLevel
        FROM dbo.ApiKey k JOIN dbo.Customer c ON c.Id=k.CustomerId
        WHERE k.Id=@Id AND k.AccessLevel=1 AND k.IsActive=1 AND k.RevokedAtUtc IS NULL
          AND (k.ExpiresAtUtc IS NULL OR k.ExpiresAtUtc>@Now) AND c.IsActive=1 AND c.DeletedAtUtc IS NULL;
        """;
}
