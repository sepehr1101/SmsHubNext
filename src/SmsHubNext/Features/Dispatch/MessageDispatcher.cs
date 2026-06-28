using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Dispatch;

/// <summary>
/// One unit of dispatch work: claim the next batch, submit its still-<c>Queued</c> messages
/// through the <see cref="ISmsProvider"/>, persist each transition, and finalize the batch.
/// All the reliability lives here in SQL (ARCHITECTURE.md §9) — the worker is just a host:
/// claims are atomic, transitions are guarded (idempotent), and a restart resumes from the DB.
///
/// Provider selection by <c>Message.ProviderId</c> is a later concern; with one provider
/// registered today every batch goes through it. Per-attempt telemetry
/// (<c>MessageBatchEvent</c>) is a follow-up.
/// </summary>
public sealed class MessageDispatcher
{
    private readonly Db _db;
    private readonly ISmsProvider _provider;
    private readonly DispatchOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<MessageDispatcher> _logger;

    public MessageDispatcher(
        Db db,
        ISmsProvider provider,
        DispatchOptions options,
        TimeProvider clock,
        ILogger<MessageDispatcher> logger)
    {
        _db = db;
        _provider = provider;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Process at most one batch. Returns <c>true</c> if a batch was claimed (so the caller
    /// should loop again promptly), <c>false</c> when there was nothing to do (idle/back off).
    /// </summary>
    public async Task<bool> DispatchNextBatchAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var heldRetryBefore = now - _options.HoldRetryDelay;
        var dispatchStaleBefore = now - _options.DispatchLeaseTimeout;

        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        var batch = await connection.QuerySingleOrDefaultAsync<ClaimedBatch>(new CommandDefinition(
            DispatchSql.ClaimNextBatch,
            new { Now = now, HeldRetryBefore = heldRetryBefore, DispatchStaleBefore = dispatchStaleBefore },
            cancellationToken: cancellationToken));

        if (batch is null)
            return false;

        var senderLine = await connection.QuerySingleAsync<string>(new CommandDefinition(
            DispatchSql.GetSenderLineNumber,
            new { batch.SenderLineId },
            cancellationToken: cancellationToken));

        var messages = (await connection.QueryAsync<QueuedMessage>(new CommandDefinition(
            DispatchSql.LoadQueuedMessages,
            new { BatchId = batch.Id },
            cancellationToken: cancellationToken))).AsList();

        _logger.LogDebug("Dispatching batch {BatchId}: {Count} queued message(s)", batch.Id, messages.Count);

        var held = false;
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var send = await SafeSendAsync(senderLine, message, cancellationToken);

            if (send.IsFailure)
            {
                // Transient/transport error: re-queue the batch and stop; a later cycle retries.
                _logger.LogWarning("Transient dispatch failure on batch {BatchId}: {Error}. Re-queuing.",
                    batch.Id, send.Error!.Message);
                await connection.ExecuteAsync(new CommandDefinition(
                    DispatchSql.RevertBatchToReceived, new { batch.Id, Now = now },
                    cancellationToken: cancellationToken));
                return true;
            }

            var outcome = send.Value;
            switch (outcome.Status)
            {
                case ProviderDispatchStatus.Accepted:
                    await connection.ExecuteAsync(new CommandDefinition(
                        DispatchSql.MarkSubmitted,
                        new { message.Id, outcome.ProviderMessageId },
                        cancellationToken: cancellationToken));
                    break;

                case ProviderDispatchStatus.Rejected:
                    await RejectAndRefundAsync(connection, batch, message, outcome, now, cancellationToken);
                    break;

                case ProviderDispatchStatus.InsufficientCredit:
                    held = true;
                    break;
            }

            if (held)
                break;
        }

        if (held)
        {
            _logger.LogInformation("Batch {BatchId} held: provider credit exhausted.", batch.Id);
            await connection.ExecuteAsync(new CommandDefinition(
                DispatchSql.HoldBatch,
                new { batch.Id, Now = now, Reason = (byte)BatchStatusReason.InsufficientProviderCredit },
                cancellationToken: cancellationToken));
            return true;
        }

        await FinalizeAsync(connection, batch.Id, now, cancellationToken);
        return true;
    }

    private async Task<Result<ProviderDispatchResult>> SafeSendAsync(
        string senderLine, QueuedMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await _provider.SendAsync(
                new ProviderSendRequest(senderLine, message.MobileNumber, message.Body), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider should not throw for expected failures; treat anything that escapes as transient.
            _logger.LogError(ex, "Provider threw dispatching message {MessageId}", message.Id);
            return Error.Provider("dispatch.provider_threw", ex.Message);
        }
    }

    private static async Task RejectAndRefundAsync(
        SqlConnection connection,
        ClaimedBatch batch,
        QueuedMessage message,
        ProviderDispatchResult outcome,
        DateTime now,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.MarkRejected,
            new { message.Id, outcome.ProviderMessageId },
            transaction,
            cancellationToken: cancellationToken));

        // Refund only if this call actually transitioned the message (idempotent under retry).
        if (changed == 1)
        {
            var balanceAfter = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                DispatchSql.CreditBalance,
                new { message.CustomerId, Amount = message.TotalCost, Now = now },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                DispatchSql.InsertRefundLedger,
                new
                {
                    message.CustomerId,
                    Type = (byte)BalanceTransactionType.Refund,
                    Amount = message.TotalCost,
                    BalanceAfter = balanceAfter ?? message.TotalCost,
                    MessageBatchId = batch.Id,
                    Reference = "provider-rejected",
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        transaction.Commit();
    }

    private static async Task FinalizeAsync(
        SqlConnection connection,
        long batchId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var counts = await connection.QuerySingleAsync<StatusCounts>(new CommandDefinition(
            DispatchSql.CountMessageStatuses,
            new { BatchId = batchId },
            cancellationToken: cancellationToken));

        var rejected = counts.Rejected ?? 0;
        var submitted = counts.Submitted ?? 0;

        var status = rejected == 0
            ? BatchStatus.Completed
            : submitted == 0
                ? BatchStatus.Failed
                : BatchStatus.PartiallyFailed;

        await connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.FinalizeBatch,
            new { Id = batchId, Status = (byte)status, Now = now },
            cancellationToken: cancellationToken));
    }

    private sealed record ClaimedBatch(long Id, short CustomerId, byte ProviderId, short SenderLineId);

    private sealed record QueuedMessage(long Id, string MobileNumber, decimal TotalCost, short CustomerId, string Body);

    private sealed record StatusCounts(int? Queued, int? Submitted, int? Rejected);
}
