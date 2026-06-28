using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Dispatch;

/// <summary>
/// One unit of dispatch work: claim the next batch, submit its still-<c>Queued</c> messages
/// through the <see cref="ISmsProvider"/> in chunks of <see cref="ISmsProvider.MaxBatchSize"/>
/// (one HTTP request per chunk), persist each message's transition individually, and finalize the
/// batch. Batching is a transport optimization only — each message keeps its own outcome,
/// <c>ProviderMessageId</c>, refund, retry, and idempotency exactly as a one-by-one dispatch would.
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
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        DateTime heldRetryBefore = now - _options.HoldRetryDelay;
        DateTime dispatchStaleBefore = now - _options.DispatchLeaseTimeout;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        ClaimedBatch? batch = await connection.QuerySingleOrDefaultAsync<ClaimedBatch>(new CommandDefinition(
            DispatchSql.ClaimNextBatch,
            new { Now = now, HeldRetryBefore = heldRetryBefore, DispatchStaleBefore = dispatchStaleBefore },
            cancellationToken: cancellationToken));

        if (batch is null)
            return false;

        string senderLine = await connection.QuerySingleAsync<string>(new CommandDefinition(
            DispatchSql.GetSenderLineNumber,
            new { batch.SenderLineId },
            cancellationToken: cancellationToken));

        List<QueuedMessage> messages = (await connection.QueryAsync<QueuedMessage>(new CommandDefinition(
            DispatchSql.LoadQueuedMessages,
            new { BatchId = batch.Id },
            cancellationToken: cancellationToken))).AsList();

        _logger.LogDebug("Dispatching batch {BatchId}: {Count} queued message(s)", batch.Id, messages.Count);

        // Hand the queued messages to the provider in chunks of at most MaxBatchSize — one HTTP
        // request per chunk — but apply each message's outcome individually, exactly as a one-by-one
        // dispatch would. Batching is a transport optimization only; the per-message state machine
        // (Submitted / Rejected+refund / retried / held) is unchanged.
        bool held = false;
        bool requeue = false;

        foreach (QueuedMessage[] chunk in messages.Chunk(Math.Max(1, _provider.MaxBatchSize)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<ProviderSendRequest> requests = chunk
                .Select(m => new ProviderSendRequest(m.Id, senderLine, m.MobileNumber, m.Body))
                .ToList();

            Result<IReadOnlyList<Result<ProviderDispatchResult>>> batchResult =
                await SafeSendBatchAsync(requests, cancellationToken);

            if (batchResult.IsFailure)
            {
                // Whole-chunk transport error: re-queue the batch and stop; a later cycle retries.
                // Messages already Submitted in earlier chunks stay Submitted (not reloaded).
                _logger.LogWarning("Transient dispatch failure on batch {BatchId}: {Error}. Re-queuing.",
                    batch.Id, batchResult.Error!.Message);
                await connection.ExecuteAsync(new CommandDefinition(
                    DispatchSql.RevertBatchToReceived, new { batch.Id, Now = now },
                    cancellationToken: cancellationToken));
                return true;
            }

            IReadOnlyList<Result<ProviderDispatchResult>> outcomes = batchResult.Value;
            if (outcomes.Count != chunk.Length)
            {
                // A provider must return one result per request; anything else is a contract breach.
                // Treat it as transient and re-queue rather than guess at the alignment.
                _logger.LogError("Provider returned {Got} result(s) for {Sent} message(s) on batch {BatchId}; re-queuing.",
                    outcomes.Count, chunk.Length, batch.Id);
                await connection.ExecuteAsync(new CommandDefinition(
                    DispatchSql.RevertBatchToReceived, new { batch.Id, Now = now },
                    cancellationToken: cancellationToken));
                return true;
            }

            for (int i = 0; i < chunk.Length; i++)
            {
                QueuedMessage message = chunk[i];
                Result<ProviderDispatchResult> send = outcomes[i];

                if (send.IsFailure)
                {
                    // Per-message transient: leave this one Queued so a later cycle retries just it.
                    requeue = true;
                    continue;
                }

                ProviderDispatchResult outcome = send.Value;
                switch (outcome.Status)
                {
                    case ProviderDispatchStatus.Accepted:
                        await connection.ExecuteAsync(new CommandDefinition(
                            DispatchSql.MarkSubmitted,
                            new { message.Id, outcome.ProviderMessageId, Now = now },
                            cancellationToken: cancellationToken));
                        break;

                    case ProviderDispatchStatus.Rejected:
                        await RejectAndRefundAsync(connection, batch, message, outcome, now, cancellationToken);
                        break;

                    case ProviderDispatchStatus.InsufficientCredit:
                        held = true; // leave this message Queued; stop after applying the rest of the chunk
                        break;
                }
            }

            if (held)
                break; // provider credit exhausted: don't send further chunks
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

        if (requeue)
        {
            // Some messages hit a per-message transient and stay Queued: re-queue so they are retried.
            await connection.ExecuteAsync(new CommandDefinition(
                DispatchSql.RevertBatchToReceived, new { batch.Id, Now = now },
                cancellationToken: cancellationToken));
            return true;
        }

        await FinalizeAsync(connection, batch.Id, now, cancellationToken);
        return true;
    }

    private async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SafeSendBatchAsync(
        IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
    {
        try
        {
            return await _provider.SendBatchAsync(requests, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider should not throw for expected failures; treat anything that escapes as transient.
            _logger.LogError(ex, "Provider threw dispatching {Count} message(s)", requests.Count);
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
        using SqlTransaction transaction = connection.BeginTransaction();

        int changed = await connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.MarkRejected,
            new { message.Id, outcome.ProviderMessageId },
            transaction,
            cancellationToken: cancellationToken));

        // Refund only if this call actually transitioned the message (idempotent under retry).
        if (changed == 1)
        {
            decimal? balanceAfter = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
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
        StatusCounts counts = await connection.QuerySingleAsync<StatusCounts>(new CommandDefinition(
            DispatchSql.CountMessageStatuses,
            new { BatchId = batchId },
            cancellationToken: cancellationToken));

        int rejected = counts.Rejected ?? 0;
        int submitted = counts.Submitted ?? 0;

        BatchStatus status = rejected == 0
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
