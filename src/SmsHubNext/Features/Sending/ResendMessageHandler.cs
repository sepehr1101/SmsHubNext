using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Sending;

/// <summary>Create a separately billed single-message batch; never reset the source lifecycle.</summary>
public sealed class ResendMessageHandler(Db db, SendMessagesHandler sending)
{
    public async Task<Result<ResendMessagePreview>> Preview(long messageId, ApiKeyIdentity identity, CancellationToken token)
    {
        if (messageId <= 0) return Error.Validation("sending.invalid_resend", "شناسه پیام معتبر نیست.");
        if (identity.AccessLevel is not (ApiKeyAccessLevel.Customer or ApiKeyAccessLevel.Administrator))
            return Error.Forbidden("sending.resend_forbidden", "اجازه ارسال مجدد ندارید.");
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        SourceMessage? source = await LoadSource(connection, messageId, identity, token);
        if (source is null) return Error.NotFound("sending.message_not_found", "پیام یافت نشد.");
        return new ResendMessagePreview(messageId, source.MobileNumber, source.Status, source.DeliveryStatus, !string.IsNullOrWhiteSpace(source.Body));
    }

    public Task<Result<ResendMessageResponse>> Handle(long messageId, ResendMessageRequest request,
        ApiKeyIdentity identity, CancellationToken token) => HandleLocked(messageId, request, identity, null, null, token);

    internal Task<Result<ResendMessageResponse>> HandleDaily(long messageId, Guid requestId, string day,
        decimal approvedCost, ApiKeyIdentity identity, CancellationToken token) =>
        HandleLocked(messageId, new ResendMessageRequest { RequestId = requestId }, identity, day, approvedCost, token);

    private async Task<Result<ResendMessageResponse>> HandleLocked(long messageId, ResendMessageRequest request,
        ApiKeyIdentity identity, string? day, decimal? approvedCost, CancellationToken token)
    {
        Result validation = request.Validate(messageId);
        if (validation.IsFailure) return validation.Error!;
        if (identity.AccessLevel is not (ApiKeyAccessLevel.Customer or ApiKeyAccessLevel.Administrator))
            return Error.Forbidden("sending.resend_forbidden", "اجازه ارسال مجدد ندارید.");

        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        string resource = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"SmsHubNext.Resend:{messageId}");
        if (!await ResendLock.TryAcquire(connection, resource, 10000, token))
            return Error.Conflict("sending.resend_busy", "ارسال مجدد این پیام در حال بررسی است؛ دوباره تلاش کنید.");
        try { return await AcceptLocked(connection, messageId, request, identity, day, approvedCost, token); }
        finally { await ResendLock.Release(connection, resource); }
    }

    private async Task<Result<ResendMessageResponse>> AcceptLocked(SqlConnection connection, long messageId,
        ResendMessageRequest request, ApiKeyIdentity identity, string? day, decimal? approvedCost, CancellationToken token)
    {
        SourceMessage? source = await LoadSource(connection, messageId, identity, token);
        // The same response for an absent message and another tenant's message.
        if (source is null) return Error.NotFound("sending.message_not_found", "پیام یافت نشد.");

        string clientBatchId = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"resend:{messageId}:{request.RequestId:N}");
        ResentMessage? existing = await FindAsync(connection, source.CustomerId, clientBatchId, token);
        if (existing is not null) return new ResendMessageResponse(messageId, existing.MessageId, existing.BatchId, true);

        if (day is not null)
        {
            bool safe = source.SubmitDateJalali == day &&
                source.ClientCorrelatedId?.StartsWith("resend:", StringComparison.Ordinal) != true &&
                await connection.ExecuteScalarAsync<bool>(new CommandDefinition(ResendMessageSql.SafeDaily,
                    new { Id = messageId, Day = day, source.MobileNumber, source.CustomerId, request.RequestId,
                        Prefix = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"resend:{messageId}:%") }, cancellationToken: token));
            if (!safe) return Error.Conflict("sending.daily_unsafe", "وضعیت پیام یا گیرنده برای ارسال مجدد روزانه ایمن نیست؛ پیام کنار گذاشته شد.");
        }
        else if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(ResendMessageSql.DailyCopyExists,
            new { Id = messageId }, cancellationToken: token)))
            return Error.Conflict("sending.daily_already_resent", "برای این پیام عملیات ارسال مجدد روزانه ثبت شده است؛ پیام جدید ایجاد نشد.");

        if (!ResendMessageRequest.IsAllowed(source.Status, source.DeliveryStatus))
            return Error.Conflict("sending.resend_not_allowed", "پیام در صف، در انتظار تأیید یا فاقد نتیجه قطعی است؛ ارسال مجدد مجاز نیست.");
        if (string.IsNullOrWhiteSpace(source.Body))
            return Error.Conflict("sending.resend_body_missing", "متن پیام دیگر نگهداری نمی‌شود؛ ارسال مجدد ممکن نیست.");

        SendMessagesRequest send = CreateRequest(source, clientBatchId);
        Result<SendMessagesResponse> accepted = await sending.AcceptResend(send, identity, source.CustomerId, token, approvedCost);
        if (accepted.IsFailure) return accepted.Error!;
        ResentMessage result = (await FindAsync(connection, source.CustomerId, clientBatchId, token))
            ?? throw new InvalidOperationException("Accepted resend batch has no message.");
        return new ResendMessageResponse(messageId, result.MessageId, result.BatchId, accepted.Value.IsDuplicate);
    }

    private static SendMessagesRequest CreateRequest(SourceMessage source, string clientBatchId) => new()
    {
        ClientBatchId = clientBatchId, SenderLine = source.SenderLine, MessageTypeId = source.MessageTypeId,
        Messages = [new SendMessageItem
        {
            Recipient = source.MobileNumber, Text = source.Body!, GeoSectionId = source.GeoSectionId,
            BillId = source.BillId, PayId = source.PayId, ClientCorrelatedId = clientBatchId,
        }],
    };

    private static Task<ResentMessage?> FindAsync(SqlConnection connection, short customerId, string clientBatchId, CancellationToken token) =>
        connection.QuerySingleOrDefaultAsync<ResentMessage>(new CommandDefinition(ResendMessageSql.Existing,
            new { CustomerId = customerId, ClientBatchId = new DbString { Value = clientBatchId, IsAnsi = true, Length = 100 } }, cancellationToken: token));

    private static Task<SourceMessage?> LoadSource(SqlConnection connection, long messageId, ApiKeyIdentity identity, CancellationToken token) =>
        connection.QuerySingleOrDefaultAsync<SourceMessage>(new CommandDefinition(ResendMessageSql.Source,
            new { Id = messageId, identity.CustomerId, Administrator = identity.AccessLevel == ApiKeyAccessLevel.Administrator }, cancellationToken: token));

    private sealed record SourceMessage(short CustomerId, string MobileNumber, string SenderLine, byte MessageTypeId,
        int? GeoSectionId, string? BillId, string? PayId, string? Body, SendStatus Status, DeliveryStatus DeliveryStatus,
        string SubmitDateJalali, string? ClientCorrelatedId);
    private sealed record ResentMessage(long MessageId, long BatchId);
}

internal static class ResendMessageSql
{
    public const string Source = """
        SELECT m.CustomerId, m.MobileNumber, s.LineNumber SenderLine, m.MessageTypeId, m.GeoSectionId,
               m.BillId, m.PayId, b.Body, m.Status, m.DeliveryStatus, m.SubmitDateJalali, m.ClientCorrelatedId
        FROM dbo.Message m
        JOIN dbo.SenderLine s ON s.Id = m.SenderLineId
        LEFT JOIN dbo.MessageBody b ON b.Id = m.Id
        WHERE m.Id = @Id AND (@Administrator = 1 OR m.CustomerId = @CustomerId);
        """;
    public const string Existing = """
        SELECT m.Id MessageId, b.Id BatchId
        FROM dbo.MessageBatch b JOIN dbo.Message m ON m.MessageBatchId = b.Id
        WHERE b.CustomerId = @CustomerId AND b.ClientBatchId = @ClientBatchId
          AND b.MessageCount = 1 AND m.ClientCorrelatedId = @ClientBatchId;
        """;
    public const string SafeDaily = """
        SELECT CAST(CASE WHEN
            EXISTS (SELECT 1 FROM dbo.DailyResendItem i JOIN dbo.DailyResendOperation o ON o.Id=i.OperationId
                WHERE i.SourceMessageId=@Id AND i.RequestId=@RequestId AND i.State=0 AND i.EstimatedCost IS NOT NULL
                  AND o.SubmitDateJalali=@Day AND o.State=2
                  AND NOT EXISTS (SELECT 1 FROM dbo.Message newer WHERE newer.SubmitDateJalali=@Day
                      AND newer.Id>o.CutoffMessageId AND newer.MobileNumber=@MobileNumber))
            AND NOT EXISTS (SELECT 1 FROM dbo.MessageBatch WHERE CustomerId = @CustomerId AND ClientBatchId LIKE @Prefix)
            AND EXISTS (SELECT 1 FROM dbo.Message m WHERE m.Id=@Id AND
        """ + "\n" + DailyResendSql.Eligibility + "\n" + """
            )
            THEN 1 ELSE 0 END AS bit);
        """;
    public const string DailyCopyExists = """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1 FROM dbo.DailyResendItem i JOIN dbo.MessageBatch b
                ON b.ClientBatchId = CONCAT('resend:', i.SourceMessageId, ':', LOWER(REPLACE(CONVERT(VARCHAR(36), i.RequestId), '-', '')))
            JOIN dbo.Message source ON source.Id = i.SourceMessageId AND source.CustomerId = b.CustomerId
            WHERE i.SourceMessageId = @Id
        ) THEN 1 ELSE 0 END AS bit);
        """;
}
