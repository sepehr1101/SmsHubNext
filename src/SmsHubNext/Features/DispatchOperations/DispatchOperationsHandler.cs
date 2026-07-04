using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.DispatchOperations;

public sealed class DispatchOperationsHandler
{
    private readonly Db _db;
    private readonly TimeProvider _clock;

    public DispatchOperationsHandler(Db db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<DispatchOperationsSummary>> Summary(
        DispatchOperationsRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate(includePaging: false);
        if (validation.IsFailure)
            return validation.Error!;

        DispatchOperationsQuery query = QueryFrom(request);
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        DispatchQueueTotals totals = await connection.QuerySingleAsync<DispatchQueueTotals>(new CommandDefinition(
            DispatchOperationsSql.Totals,
            query,
            cancellationToken: cancellationToken));

        IEnumerable<BatchStatusCountRow> batchStatusRows =
            await connection.QueryAsync<BatchStatusCountRow>(new CommandDefinition(
                DispatchOperationsSql.BatchStatuses,
                query,
                cancellationToken: cancellationToken));

        IEnumerable<MessageStatusCountRow> messageStatusRows =
            await connection.QueryAsync<MessageStatusCountRow>(new CommandDefinition(
                DispatchOperationsSql.MessageStatuses,
                query,
                cancellationToken: cancellationToken));

        IEnumerable<FailureReasonCountRow> failureReasonRows =
            await connection.QueryAsync<FailureReasonCountRow>(new CommandDefinition(
                DispatchOperationsSql.FailureReasons,
                query,
                cancellationToken: cancellationToken));

        return new DispatchOperationsSummary(
            totals,
            batchStatusRows
                .Select(row => new DispatchBatchStatusCount((BatchStatus)row.Status, row.BatchCount, row.MessageCount))
                .ToList(),
            messageStatusRows
                .Select(row => new DispatchMessageStatusCount((SendStatus)row.Status, row.MessageCount))
                .ToList(),
            failureReasonRows
                .Select(row => new DispatchFailureReasonCount((BatchStatusReason)row.Reason, row.BatchCount))
                .ToList());
    }

    public async Task<Result<DispatchOperationsBatchPage>> Batches(
        DispatchOperationsRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate(includePaging: true);
        if (validation.IsFailure)
            return validation.Error!;

        DispatchOperationsQuery query = QueryFrom(request);
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        long totalCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            DispatchOperationsSql.CountBatches,
            query,
            cancellationToken: cancellationToken));

        IEnumerable<BatchRow> rows =
            await connection.QueryAsync<BatchRow>(new CommandDefinition(
                DispatchOperationsSql.ListBatches,
                query,
                cancellationToken: cancellationToken));

        IReadOnlyList<DispatchOperationsBatchRow> items = rows
            .Select(row => new DispatchOperationsBatchRow(
                row.Id,
                row.SubmitDateJalali,
                row.ReceivedAtUtc,
                row.CustomerId,
                row.ProviderId,
                row.ProviderCode,
                row.ProviderName,
                row.MessageCount,
                row.SegmentCount,
                row.TotalCost,
                (BatchStatus)row.Status,
                row.StatusReason is null ? null : (BatchStatusReason)row.StatusReason.Value,
                row.DispatchAttemptCount,
                row.NextDispatchAtUtc,
                row.StatusChangedAtUtc,
                row.QueuedMessageCount,
                row.AwaitingConfirmationMessageCount,
                row.RejectedMessageCount,
                row.LastEventType is null ? null : (MessageBatchEventType)row.LastEventType.Value,
                row.LastEventTimeUtc,
                row.LastEventDetail))
            .ToList();

        return new DispatchOperationsBatchPage(request.Page, request.Take, totalCount, items);
    }

    private DispatchOperationsQuery QueryFrom(DispatchOperationsRequest request) => new(
        request.FromJalali,
        request.ToJalali,
        request.CustomerId,
        request.ProviderId,
        request.Status is null ? null : (byte?)request.Status.Value,
        request.OnlyProblems,
        request.Offset,
        request.Take,
        _clock.GetUtcNow().UtcDateTime);

    private sealed record DispatchOperationsQuery(
        string? FromJalali,
        string? ToJalali,
        short? CustomerId,
        byte? ProviderId,
        byte? Status,
        bool OnlyProblems,
        int Offset,
        int Take,
        DateTime Now);

    private sealed record BatchStatusCountRow(byte Status, long BatchCount, long MessageCount);

    private sealed record MessageStatusCountRow(byte Status, long MessageCount);

    private sealed record FailureReasonCountRow(byte Reason, long BatchCount);

    private sealed record BatchRow(
        long Id,
        string SubmitDateJalali,
        DateTime ReceivedAtUtc,
        short CustomerId,
        byte ProviderId,
        string ProviderCode,
        string ProviderName,
        int MessageCount,
        int SegmentCount,
        decimal TotalCost,
        byte Status,
        byte? StatusReason,
        int DispatchAttemptCount,
        DateTime? NextDispatchAtUtc,
        DateTime StatusChangedAtUtc,
        long QueuedMessageCount,
        long AwaitingConfirmationMessageCount,
        long RejectedMessageCount,
        byte? LastEventType,
        DateTime? LastEventTimeUtc,
        string? LastEventDetail);
}
