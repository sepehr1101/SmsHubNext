using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Reports;

public sealed class MessageDiagnosticsHandler(Db db, SmsProviderRegistry providers, TimeProvider clock)
{
    public async Task<Result<MessageDiagnostics>> Handle(long messageId, bool queryProvider, CancellationToken token)
    {
        if (messageId <= 0) return Error.Validation("reports.invalid_message", "شناسه پیام معتبر نیست.");
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        using SqlMapper.GridReader results = await connection.QueryMultipleAsync(new CommandDefinition(
            MessageDiagnosticsSql.Read, new { MessageId = messageId }, cancellationToken: token));
        MessageDiagnosticRow? message = await results.ReadSingleOrDefaultAsync<MessageDiagnosticRow>();
        if (message is null) return Error.NotFound("reports.message_not_found", "پیام یافت نشد.");
        List<DiagnosticDelivery> deliveries = (await results.ReadAsync<DiagnosticDelivery>()).AsList();
        List<DiagnosticEvent> events = (await results.ReadAsync<DiagnosticEvent>()).AsList();
        MessageDiagnostics report = new(message, deliveries, events, clock.GetUtcNow().UtcDateTime);
        if (!queryProvider) return report;
        if (string.IsNullOrWhiteSpace(message.ProviderMessageId))
            return report with { LiveError = "شناسه سرویس ثبت نشده است؛ استعلام تحویل ممکن نیست. رویدادهای دسته را بررسی کنید." };
        Result<ISmsProvider> provider = providers.Resolve(message.ProviderCode);
        if (provider.IsFailure) return report with { LiveError = provider.Error!.Message };
        Result<IReadOnlyList<ProviderDeliveryReport>> live = await provider.Value.GetDeliveryReportsAsync([message.ProviderMessageId], token);
        DateTime observed = clock.GetUtcNow().UtcDateTime;
        if (live.IsFailure) return report with { LiveError = live.Error!.Message,
            LiveFailure = live.Error.Diagnostic, LiveObservedAtUtc = observed };
        ProviderDeliveryReport? status = live.Value.FirstOrDefault(item => item.ProviderMessageId == message.ProviderMessageId);
        return report with { Live = status, LiveObservedAtUtc = observed,
            LiveError = status is null || !status.HasProviderStatus ? "در پاسخ سرویس، وضعیتی برای این شناسه وجود نداشت." : null };
    }
}

public sealed record MessageDiagnosticRow(long Id, long MessageBatchId, string SubmitDateJalali, DateTime SubmittedAtUtc,
    string MobileNumber, string ProviderCode, string ProviderName, string SenderLine, string? ProviderMessageId,
    string? ClientCorrelatedId, string? ClientBatchId, SendStatus Status, DeliveryStatus DeliveryStatus,
    DateTime? DeliveredAtUtc, BatchStatus BatchStatus, BatchStatusReason? StatusReason, int? ProviderResultCode,
    DateTime? DispatchStartedAtUtc, DateTime? FinishedAtUtc, DateTime StatusChangedAtUtc,
    int DispatchAttemptCount, DateTime? NextDispatchAtUtc, DateTime? AwaitingConfirmationSinceUtc,
    DateTime? DispatchedAtUtc, DateTime? NextPollAtUtc, int? PollAttempts);
public sealed record DiagnosticDelivery(int RawStatusCode, DeliveryReportStatus NormalizedStatus, DateTime ReceivedAtUtc);
public sealed record DiagnosticEvent(MessageBatchEventType EventType, int? ProviderResultCode, string? Detail, DateTime EventTimeUtc, string? FailureJson = null)
{
    public OperationFailure? Failure => OperationFailure.FromJson(FailureJson);
}
public sealed record MessageDiagnostics(MessageDiagnosticRow Message, IReadOnlyList<DiagnosticDelivery> Deliveries,
    IReadOnlyList<DiagnosticEvent> Events, DateTime ReadAtUtc)
{
    public ProviderDeliveryReport? Live { get; init; }
    public DateTime? LiveObservedAtUtc { get; init; }
    public string? LiveError { get; init; }
    public OperationFailure? LiveFailure { get; init; }
}

internal static class MessageDiagnosticsSql
{
    public const string Read = """
        SELECT m.Id, m.MessageBatchId, m.SubmitDateJalali, m.SubmittedAtUtc, m.MobileNumber,
               p.Code AS ProviderCode, p.Name AS ProviderName, s.LineNumber AS SenderLine,
               m.ProviderMessageId, m.ClientCorrelatedId, b.ClientBatchId, m.Status, m.DeliveryStatus,
               m.DeliveredAtUtc, b.Status AS BatchStatus, b.StatusReason, b.ProviderResultCode,
               b.DispatchStartedAtUtc, b.FinishedAtUtc, b.StatusChangedAtUtc, b.DispatchAttemptCount,
               b.NextDispatchAtUtc, m.AwaitingConfirmationSinceUtc,
               q.DispatchedAtUtc, q.NextPollAtUtc, q.Attempts AS PollAttempts
        FROM dbo.Message m
        INNER JOIN dbo.MessageBatch b ON b.Id = m.MessageBatchId
        INNER JOIN dbo.Provider p ON p.Id = m.ProviderId
        INNER JOIN dbo.SenderLine s ON s.Id = m.SenderLineId
        LEFT JOIN dbo.DeliveryReportPoll q ON q.MessageId = m.Id
        WHERE m.Id = @MessageId;

        SELECT TOP (100) RawStatusCode, NormalizedStatus, ReceivedAtUtc
        FROM dbo.DeliveryReport WHERE MessageId = @MessageId ORDER BY ReceivedAtUtc DESC, Id DESC;

        SELECT TOP (100) EventType, ProviderResultCode, Detail, EventTimeUtc, FailureJson
        FROM dbo.MessageBatchEvent
        WHERE MessageBatchId = (SELECT MessageBatchId FROM dbo.Message WHERE Id = @MessageId)
        ORDER BY EventTimeUtc DESC, Id DESC;
        """;
}
