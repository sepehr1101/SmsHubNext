using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.DeliveryReports;

/// <summary>
/// Ingests a delivery report: appends it to the immutable <c>DeliveryReport</c> history
/// and updates the denormalized <c>Message.DeliveryStatus</c> read model, atomically
/// (README §4.10/§4.12/§7.4). The append-only history is the source of truth; the read
/// model is what makes the join-free success rate possible.
/// </summary>
public sealed class IngestDeliveryReportHandler
{
    private readonly Db _db;
    private readonly TimeProvider _clock;

    public IngestDeliveryReportHandler(Db db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<IngestDeliveryReportResponse>> Handle(
        IngestDeliveryReportRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        // The report copies the message's partition key, so it must exist first.
        MessagePartition? partition = await connection.QuerySingleOrDefaultAsync<MessagePartition>(new CommandDefinition(
            DeliveryReportsSql.GetMessagePartition,
            new { request.MessageId },
            cancellationToken: cancellationToken));

        if (partition is null)
            return Error.NotFound("delivery_reports.unknown_message", "The message does not exist.");

        DeliveryStatus readModel = request.Status.ToDeliveryStatus();
        DateTime receivedAtUtc = _clock.GetUtcNow().UtcDateTime;

        using SqlTransaction transaction = connection.BeginTransaction();

        long reportId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            DeliveryReportsSql.InsertReport,
            new
            {
                partition.SubmitDateJalali,
                request.MessageId,
                NormalizedStatus = (byte)request.Status,
                request.RawStatusCode,
                ReceivedAtUtc = receivedAtUtc,
            },
            transaction,
            cancellationToken: cancellationToken));

        int updated = await connection.ExecuteAsync(new CommandDefinition(
            DeliveryReportsSql.UpdateMessageStatus,
            new
            {
                DeliveryStatus = (byte)readModel,
                DeliveredValue = (byte)DeliveryStatus.Delivered,
                PendingValue = (byte)DeliveryStatus.Pending,
                ReceivedAtUtc = receivedAtUtc,
                request.MessageId,
            },
            transaction,
            cancellationToken: cancellationToken));

        bool applied = updated == 1;
        DeliveryStatus responseStatus = applied ? readModel : partition.DeliveryStatus;
        string detail = applied
            ? $"Delivery report applied for message {request.MessageId}: {readModel}."
            : $"Delivery report recorded for message {request.MessageId}: {readModel}; read model already terminal as {partition.DeliveryStatus}.";

        await connection.ExecuteAsync(new CommandDefinition(
            DeliveryReportsSql.InsertBatchEventForReport,
            new
            {
                partition.MessageBatchId,
                ReceivedAtUtc = receivedAtUtc,
                EventType = (byte)MessageBatchEventType.DeliveryUpdated,
                Detail = detail,
            },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return new IngestDeliveryReportResponse(reportId, responseStatus, applied);
    }

    private sealed record MessagePartition(string SubmitDateJalali, long MessageBatchId, DeliveryStatus DeliveryStatus);
}
