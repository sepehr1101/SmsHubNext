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
            DispatchSql.LoadDispatchableMessages,
            new { BatchId = batch.Id },
            cancellationToken: cancellationToken))).AsList();

        _logger.LogDebug("Dispatching batch {BatchId}: {Count} message(s)", batch.Id, messages.Count);

        // Reconcile any message whose previous send response was lost (AwaitingConfirmation): confirm
        // it via the provider rather than blindly re-sending, so a lost response never double-charges.
        // The rest (Queued) go straight to the send phase.
        List<QueuedMessage> toSend = new List<QueuedMessage>(messages.Count);
        foreach (QueuedMessage message in messages)
        {
            if (message.Status != (byte)SendStatus.AwaitingConfirmation)
            {
                toSend.Add(message);
                continue;
            }

            Result<string?> lookup = await SafeResolveAsync(message.Id, cancellationToken);
            if (lookup.IsFailure)
            {
                // Can't confirm right now: re-queue and retry the whole batch on a later cycle.
                await RequeueBatchAsync(connection, batch.Id, now, cancellationToken);
                return true;
            }

            if (lookup.Value is string providerMessageId)
            {
                // The provider had already accepted it — confirm Submitted without re-sending.
                await MarkSubmittedAsync(connection, message.Id, providerMessageId, now, cancellationToken);
            }
            else
            {
                // No provider record — it was never accepted, so reset to Queued and send it below.
                await connection.ExecuteAsync(new CommandDefinition(
                    DispatchSql.RequeueMessage, new { message.Id }, cancellationToken: cancellationToken));
                toSend.Add(message);
            }
        }

        // Hand the remaining messages to the provider in chunks of at most MaxBatchSize — one HTTP
        // request per chunk — but apply each message's outcome individually, exactly as a one-by-one
        // dispatch would. Batching is a transport optimization only; the per-message state machine
        // (Submitted / Rejected+refund / retried / held / awaiting-confirmation) is unchanged.
        bool held = false;
        bool requeue = false;

        foreach (QueuedMessage[] chunk in toSend.Chunk(_provider.MaxBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<ProviderSendRequest> requests = chunk
                .Select(m => new ProviderSendRequest(m.Id, senderLine, m.MobileNumber, m.Body))
                .ToList();

            Result<IReadOnlyList<Result<ProviderDispatchResult>>> batchResult =
                await SafeSendBatchAsync(requests, cancellationToken);

            if (batchResult.IsFailure)
            {
                // Transport error: the chunk's outcome is unknown. Park these messages so the next
                // cycle reconciles them via the provider rather than re-sending, then re-queue.
                _logger.LogWarning("Transient dispatch failure on batch {BatchId}: {Error}. Re-queuing.",
                    batch.Id, batchResult.Error!.Message);
                await MarkAwaitingConfirmationAsync(connection, chunk, cancellationToken);
                await RequeueBatchAsync(connection, batch.Id, now, cancellationToken);
                return true;
            }

            // The provider returns one result per request, in input order (ISmsProvider contract).
            IReadOnlyList<Result<ProviderDispatchResult>> outcomes = batchResult.Value;
            for (int i = 0; i < chunk.Length; i++)
            {
                QueuedMessage message = chunk[i];
                Result<ProviderDispatchResult> send = outcomes[i];

                if (send.IsFailure)
                {
                    // Per-message transient (provider said "busy"): leave it Queued — it was not sent,
                    // so a later cycle safely re-sends it (no confirmation needed).
                    requeue = true;
                    continue;
                }

                ProviderDispatchResult outcome = send.Value;
                switch (outcome.Status)
                {
                    case ProviderDispatchStatus.Accepted:
                        await MarkSubmittedAsync(connection, message.Id, outcome.ProviderMessageId!, now, cancellationToken);
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
            await RequeueBatchAsync(connection, batch.Id, now, cancellationToken);
            return true;
        }

        await FinalizeAsync(connection, batch.Id, now, cancellationToken);
        return true;
    }

    // Return a batch to Received so a later cycle re-claims it and retries its still-Queued messages.
    private static Task RequeueBatchAsync(
        SqlConnection connection, long batchId, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.RevertBatchToReceived, new { Id = batchId, Now = now },
            cancellationToken: cancellationToken));

    // Mark a message Submitted (from Queued or a reconciled AwaitingConfirmation) and enqueue its
    // delivery-report poll. Used both by a fresh accept and by a lookup-confirmed send.
    private static Task MarkSubmittedAsync(
        SqlConnection connection, long messageId, string providerMessageId, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.MarkSubmitted,
            new { Id = messageId, ProviderMessageId = providerMessageId, Now = now },
            cancellationToken: cancellationToken));

    // Park a chunk's still-Queued messages as AwaitingConfirmation after a lost send response.
    private static Task MarkAwaitingConfirmationAsync(
        SqlConnection connection, IReadOnlyList<QueuedMessage> chunk, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.MarkAwaitingConfirmation,
            new { Ids = chunk.Select(m => m.Id).ToArray() },
            cancellationToken: cancellationToken));

    private async Task<Result<string?>> SafeResolveAsync(long messageId, CancellationToken cancellationToken)
    {
        try
        {
            return await _provider.ResolveSubmittedMessageIdAsync(messageId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider should not throw for expected failures; treat anything that escapes as transient.
            _logger.LogError(ex, "Provider threw resolving message {MessageId}", messageId);
            return Error.Provider("dispatch.provider_threw", ex.Message);
        }
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

    private sealed record QueuedMessage(
        long Id, string MobileNumber, decimal TotalCost, short CustomerId, string Body, byte Status);

    private sealed record StatusCounts(int? Queued, int? Submitted, int? Rejected);
}
