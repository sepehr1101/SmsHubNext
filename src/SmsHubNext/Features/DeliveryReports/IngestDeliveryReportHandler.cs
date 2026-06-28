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

    public IngestDeliveryReportHandler(Db db) => _db = db;

    public async Task<Result<IngestDeliveryReportResponse>> Handle(
        IngestDeliveryReportRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        // The report copies the message's partition key, so it must exist first.
        string? submitDateJalali = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            DeliveryReportsSql.GetMessagePartition,
            new { request.MessageId },
            cancellationToken: cancellationToken));

        if (submitDateJalali is null)
            return Error.NotFound("delivery_reports.unknown_message", "The message does not exist.");

        DeliveryStatus readModel = request.Status.ToDeliveryStatus();
        DateTime receivedAtUtc = DateTime.UtcNow;

        using SqlTransaction transaction = connection.BeginTransaction();

        long reportId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            DeliveryReportsSql.InsertReport,
            new
            {
                SubmitDateJalali = submitDateJalali,
                request.MessageId,
                NormalizedStatus = (byte)request.Status,
                request.RawStatusCode,
                ReceivedAtUtc = receivedAtUtc,
            },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            DeliveryReportsSql.UpdateMessageStatus,
            new
            {
                DeliveryStatus = (byte)readModel,
                DeliveredValue = (byte)DeliveryStatus.Delivered,
                ReceivedAtUtc = receivedAtUtc,
                request.MessageId,
            },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return new IngestDeliveryReportResponse(reportId, readModel);
    }
}
