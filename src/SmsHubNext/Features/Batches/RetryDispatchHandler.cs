using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Batches;

/// <summary>Operator action: re-enter a terminal dispatch failure into the dispatch queue.</summary>
public sealed class RetryDispatchHandler
{
    private readonly Db _db;
    private readonly TimeProvider _clock;

    public RetryDispatchHandler(Db db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<RetryDispatchResponse>> Handle(long batchId, CancellationToken cancellationToken)
    {
        if (batchId <= 0)
            return Error.Validation("batches.invalid_id", "A valid batch id is required.");

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        using SqlTransaction transaction = connection.BeginTransaction();

        RetryDispatchDebit? debit = await connection.QuerySingleOrDefaultAsync<RetryDispatchDebit>(new CommandDefinition(
            DispatchSql.GetRetryableDispatchDebit,
            new { Id = batchId },
            transaction,
            cancellationToken: cancellationToken));

        if (debit is null)
        {
            transaction.Rollback();

            Batch? batch = await connection.QuerySingleOrDefaultAsync<Batch>(new CommandDefinition(
                BatchesSql.GetById,
                new { Id = batchId },
                cancellationToken: cancellationToken));

            if (batch is null)
                return Error.NotFound("batches.not_found", "The batch does not exist.");

            return Error.Conflict(
                "batches.retry_not_allowed",
                "Only dispatch-failed batches with queued or awaiting-confirmation messages can be retried.");
        }

        if (debit.TotalCost > 0)
        {
            decimal? balanceAfter = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                DispatchSql.DebitBalance,
                new { debit.CustomerId, Amount = debit.TotalCost, Now = now },
                transaction,
                cancellationToken: cancellationToken));

            if (balanceAfter is null)
            {
                transaction.Rollback();
                return Error.Validation(
                    "batches.insufficient_balance_for_retry",
                    "The customer balance is not sufficient to retry this failed dispatch.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                DispatchSql.InsertRefundLedger,
                new
                {
                    debit.CustomerId,
                    Type = (byte)BalanceTransactionType.Debit,
                    Amount = debit.TotalCost,
                    BalanceAfter = balanceAfter.Value,
                    MessageBatchId = batchId,
                    Reference = "dispatch-retry",
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        long? retriedBatchId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            DispatchSql.RetryFailedBatch,
            new { Id = batchId, Now = now },
            transaction,
            cancellationToken: cancellationToken));

        if (retriedBatchId is null)
        {
            transaction.Rollback();

            Batch? batch = await connection.QuerySingleOrDefaultAsync<Batch>(new CommandDefinition(
                BatchesSql.GetById,
                new { Id = batchId },
                cancellationToken: cancellationToken));

            if (batch is null)
                return Error.NotFound("batches.not_found", "The batch does not exist.");

            return Error.Conflict(
                "batches.retry_not_allowed",
                "Only dispatch-failed batches with queued or awaiting-confirmation messages can be retried.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.InsertBatchEvent,
            new
            {
                MessageBatchId = batchId,
                Now = now,
                EventType = (byte)MessageBatchEventType.DispatchRetryRequested,
                BatchStatus = (byte)BatchStatus.Received,
                BatchStatusReason = (byte?)null,
                ProviderResultCode = (int?)null,
                Detail = "Manual dispatch retry requested by an operator.",
            },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return new RetryDispatchResponse(batchId);
    }

    private sealed record RetryDispatchDebit(short CustomerId, long MessageCount, decimal TotalCost);
}
