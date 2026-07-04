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
/// registered today every batch goes through it. Operator-facing state changes are appended
/// to <c>MessageBatchEvent</c> so held/retried/failed batches have a readable timeline.
/// </summary>
public sealed class MessageDispatcher
{
    private readonly Db _db;
    private readonly SmsProviderRegistry _providers;
    private readonly DispatchOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<MessageDispatcher> _logger;

    public MessageDispatcher(
        Db db,
        SmsProviderRegistry providers,
        DispatchOptions options,
        TimeProvider clock,
        ILogger<MessageDispatcher> logger)
    {
        _db = db;
        _providers = providers;
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

        string providerCode = await connection.QuerySingleAsync<string>(new CommandDefinition(
            DispatchSql.GetProviderCode,
            new { batch.ProviderId },
            cancellationToken: cancellationToken));

        Result<ISmsProvider> providerResult = _providers.Resolve(providerCode);
        if (providerResult.IsFailure)
        {
            await FailUnregisteredProviderBatchAsync(
                connection, batch, now, providerResult.Error!.Message, cancellationToken);
            return true;
        }
        ISmsProvider provider = providerResult.Value;

        MessageBatchEventType claimEventType = batch.PreviousStatus == (byte)BatchStatus.Received
            ? MessageBatchEventType.DispatchStarted
            : MessageBatchEventType.DispatchResumed;
        string claimDetail = claimEventType == MessageBatchEventType.DispatchStarted
            ? "Dispatch started."
            : "Dispatch resumed after a hold or expired dispatch lease.";
        await InsertBatchEventAsync(
            connection, batch.Id, now, claimEventType, BatchStatus.Dispatching, null, null, claimDetail, cancellationToken);

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

            if (AwaitingConfirmationWindowExpired(message, now))
            {
                await HoldForManualReviewAsync(
                    connection,
                    batch,
                    now,
                    $"Message {message.Id} has been awaiting provider confirmation for longer than {_options.AwaitingConfirmationMaxAge}. Manual review is required before any resend.",
                    cancellationToken);
                return true;
            }

            if (ShouldWaitBeforeConfirmationLookup(message, now, out DateTime nextLookupAtUtc))
            {
                await RequeueAwaitingConfirmationOrHoldAsync(
                    connection,
                    batch,
                    now,
                    nextLookupAtUtc,
                    $"Message {message.Id} is awaiting confirmation; delaying lookup until {nextLookupAtUtc:O}.",
                    cancellationToken);
                return true;
            }

            Result<string?> lookup = await SafeResolveAsync(provider, message.Id, cancellationToken);
            if (lookup.IsFailure)
            {
                // Can't confirm right now: re-queue and retry the whole batch on a later cycle.
                await RequeueAwaitingConfirmationOrHoldAsync(
                    connection,
                    batch,
                    now,
                    now + _options.AwaitingConfirmationRetryDelay,
                    $"Provider confirmation lookup failed: {lookup.Error!.Message}.",
                    cancellationToken);
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
                int negativeConfirmations = await CountNegativeConfirmationAsync(connection, message.Id, cancellationToken);
                if (negativeConfirmations < _options.RequiredNegativeConfirmations)
                {
                    await RequeueAwaitingConfirmationOrHoldAsync(
                        connection,
                        batch,
                        now,
                        now + _options.AwaitingConfirmationRetryDelay,
                        $"Provider has no record for message {message.Id}; waiting for {negativeConfirmations}/{_options.RequiredNegativeConfirmations} negative confirmation(s).",
                        cancellationToken);
                    return true;
                }

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

        foreach (QueuedMessage[] chunk in toSend.Chunk(provider.MaxBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await MarkAwaitingConfirmationAsync(connection, chunk, now, cancellationToken);
            await InsertBatchEventAsync(
                connection,
                batch.Id,
                now,
                MessageBatchEventType.AwaitingConfirmation,
                BatchStatus.Dispatching,
                null,
                null,
                $"Marked {chunk.Length} message(s) as awaiting provider confirmation before submit.",
                cancellationToken);

            List<ProviderSendRequest> requests = chunk
                .Select(m => new ProviderSendRequest(m.Id, senderLine, m.MobileNumber, m.Body))
                .ToList();

            Result<IReadOnlyList<Result<ProviderDispatchResult>>> batchResult =
                await SafeSendBatchAsync(provider, requests, cancellationToken);

            if (batchResult.IsFailure)
            {
                // Transport error: the chunk's outcome is unknown. Park these messages so the next
                // cycle reconciles them via the provider rather than re-sending, then re-queue.
                _logger.LogWarning("Transient dispatch failure on batch {BatchId}: {Error}. Re-queuing.",
                    batch.Id, batchResult.Error!.Message);
                await RequeueAwaitingConfirmationOrHoldAsync(
                    connection,
                    batch,
                    now,
                    now + _options.AwaitingConfirmationRetryDelay,
                    $"Provider send response was lost for {chunk.Length} message(s): {batchResult.Error.Message}.",
                    cancellationToken);
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
                        await connection.ExecuteAsync(new CommandDefinition(
                            DispatchSql.RequeueMessage, new { message.Id }, cancellationToken: cancellationToken));
                        held = true;
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
            await InsertBatchEventAsync(
                connection,
                batch.Id,
                now,
                MessageBatchEventType.Held,
                BatchStatus.Held,
                BatchStatusReason.InsufficientProviderCredit,
                null,
                "Provider credit is exhausted; batch held and will be retried later.",
                cancellationToken);
            return true;
        }

        if (requeue)
        {
            // Some messages hit a per-message transient and stay Queued: re-queue so they are retried.
            await RequeueAwaitingConfirmationOrHoldAsync(
                connection,
                batch,
                now,
                now + _options.AwaitingConfirmationRetryDelay,
                "One or more messages had an inconclusive provider result.",
                cancellationToken);
            return true;
        }

        await FinalizeAsync(connection, batch.Id, now, cancellationToken);
        return true;
    }

    // Return a batch to Received so a later cycle re-claims it and retries its still-Queued messages.
    private static Task RequeueBatchAsync(
        SqlConnection connection, long batchId, DateTime now, DateTime nextDispatchAtUtc, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.RevertBatchToReceived, new { Id = batchId, Now = now, NextDispatchAtUtc = nextDispatchAtUtc },
            cancellationToken: cancellationToken));

    private static async Task RequeueAwaitingConfirmationBatchAsync(
        SqlConnection connection,
        ClaimedBatch batch,
        DateTime now,
        DateTime nextDispatchAtUtc,
        string detail,
        CancellationToken cancellationToken)
    {
        await RequeueBatchAsync(connection, batch.Id, now, nextDispatchAtUtc, cancellationToken);
        await InsertBatchEventAsync(
            connection,
            batch.Id,
            now,
            MessageBatchEventType.Requeued,
            BatchStatus.Received,
            null,
            null,
            $"{detail} Batch requeued for confirmation at {nextDispatchAtUtc:O}.",
            cancellationToken);
    }

    private async Task RequeueAwaitingConfirmationOrHoldAsync(
        SqlConnection connection,
        ClaimedBatch batch,
        DateTime now,
        DateTime nextDispatchAtUtc,
        string detail,
        CancellationToken cancellationToken)
    {
        if (batch.DispatchAttemptCount >= _options.MaxDispatchAttempts)
        {
            await HoldForManualReviewAsync(
                connection,
                batch,
                now,
                $"{detail} Confirmation retry limit exhausted after {batch.DispatchAttemptCount} attempt(s); manual review is required before any resend.",
                cancellationToken);
            return;
        }

        await RequeueAwaitingConfirmationBatchAsync(
            connection, batch, now, nextDispatchAtUtc, detail, cancellationToken);
    }

    private static async Task HoldForManualReviewAsync(
        SqlConnection connection,
        ClaimedBatch batch,
        DateTime now,
        string detail,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.HoldBatch,
            new { batch.Id, Now = now, Reason = (byte)BatchStatusReason.ManualReviewRequired },
            cancellationToken: cancellationToken));

        await InsertBatchEventAsync(
            connection,
            batch.Id,
            now,
            MessageBatchEventType.Held,
            BatchStatus.Held,
            BatchStatusReason.ManualReviewRequired,
            null,
            detail,
            cancellationToken);
    }

    private static Task FailUnregisteredProviderBatchAsync(
        SqlConnection connection,
        ClaimedBatch batch,
        DateTime now,
        string detail,
        CancellationToken cancellationToken) =>
        FailBatchAndRefundDispatchableAsync(
            connection, batch, now, BatchStatusReason.DispatchRetriesExhausted, detail, cancellationToken);

    private static async Task FailBatchAndRefundDispatchableAsync(
        SqlConnection connection,
        ClaimedBatch batch,
        DateTime now,
        BatchStatusReason reason,
        string detail,
        CancellationToken cancellationToken)
    {
        using SqlTransaction transaction = connection.BeginTransaction();

        DispatchableMoney? refund = await connection.QuerySingleOrDefaultAsync<DispatchableMoney>(new CommandDefinition(
            DispatchSql.GetDispatchableRefund,
            new { BatchId = batch.Id },
            transaction,
            cancellationToken: cancellationToken));

        string eventDetail = detail;
        if (refund is not null && refund.TotalCost > 0)
        {
            decimal? balanceAfter = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                DispatchSql.CreditBalance,
                new { refund.CustomerId, Amount = refund.TotalCost, Now = now },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                DispatchSql.InsertRefundLedger,
                new
                {
                    refund.CustomerId,
                    Type = (byte)BalanceTransactionType.Refund,
                    Amount = refund.TotalCost,
                    BalanceAfter = balanceAfter ?? refund.TotalCost,
                    MessageBatchId = batch.Id,
                    Reference = "dispatch-failed",
                },
                transaction,
                cancellationToken: cancellationToken));

            eventDetail = $"{detail} Refunded {refund.TotalCost:0.####} IRR for {refund.MessageCount} unsubmitted message(s).";
        }

        await connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.FailBatch,
            new { batch.Id, Now = now, Reason = (byte)reason },
            transaction,
            cancellationToken: cancellationToken));

        await InsertBatchEventAsync(
            connection,
            transaction,
            batch.Id,
            now,
            MessageBatchEventType.DispatchFailed,
            BatchStatus.DispatchFailed,
            reason,
            null,
            eventDetail,
            cancellationToken);

        transaction.Commit();
    }

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
        SqlConnection connection, IReadOnlyList<QueuedMessage> chunk, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.MarkAwaitingConfirmation,
            new { Ids = chunk.Select(m => m.Id).ToArray(), Now = now },
            cancellationToken: cancellationToken));

    private static async Task<int> CountNegativeConfirmationAsync(
        SqlConnection connection, long messageId, CancellationToken cancellationToken)
    {
        int? count = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            DispatchSql.CountNegativeConfirmation,
            new { Id = messageId },
            cancellationToken: cancellationToken));

        return count ?? 0;
    }

    private bool ShouldWaitBeforeConfirmationLookup(
        QueuedMessage message,
        DateTime now,
        out DateTime nextLookupAtUtc)
    {
        DateTime since = message.AwaitingConfirmationSinceUtc ?? now;
        nextLookupAtUtc = since + _options.MinAwaitingConfirmationAge;
        return nextLookupAtUtc > now;
    }

    private bool AwaitingConfirmationWindowExpired(QueuedMessage message, DateTime now)
    {
        if (message.AwaitingConfirmationSinceUtc is not DateTime since)
            return false;

        return since + _options.AwaitingConfirmationMaxAge <= now;
    }

    private async Task<Result<string?>> SafeResolveAsync(
        ISmsProvider provider, long messageId, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.ResolveSubmittedMessageIdAsync(messageId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider should not throw for expected failures; treat anything that escapes as transient.
            _logger.LogError(ex, "Provider threw resolving message {MessageId}", messageId);
            return Error.Provider("dispatch.provider_threw", ex.Message);
        }
    }

    private async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SafeSendBatchAsync(
        ISmsProvider provider,
        IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.SendBatchAsync(requests, cancellationToken);
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

            await InsertBatchEventAsync(
                connection,
                transaction,
                batch.Id,
                now,
                MessageBatchEventType.MessageRejected,
                BatchStatus.Dispatching,
                null,
                outcome.ProviderResultCode,
                $"Message {message.Id} rejected by provider; refunded {message.TotalCost:0.####} IRR.",
                cancellationToken);
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
            ? BatchStatus.DispatchCompleted
            : submitted == 0
                ? BatchStatus.DispatchFailed
                : BatchStatus.DispatchPartiallyFailed;

        await connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.FinalizeBatch,
            new { Id = batchId, Status = (byte)status, Now = now },
            cancellationToken: cancellationToken));

        await InsertBatchEventAsync(
            connection,
            batchId,
            now,
            EventTypeFor(status),
            status,
            null,
            null,
            $"Batch finalized as {status}: {submitted} submitted, {rejected} rejected.",
            cancellationToken);
    }

    private static MessageBatchEventType EventTypeFor(BatchStatus status) => status switch
    {
        BatchStatus.DispatchCompleted => MessageBatchEventType.DispatchCompleted,
        BatchStatus.DispatchPartiallyFailed => MessageBatchEventType.DispatchPartiallyFailed,
        BatchStatus.DispatchFailed => MessageBatchEventType.DispatchFailed,
        _ => throw new InvalidOperationException($"Batch status {status} is not a dispatch finalization event."),
    };

    private static Task InsertBatchEventAsync(
        SqlConnection connection,
        long batchId,
        DateTime now,
        MessageBatchEventType eventType,
        BatchStatus? batchStatus,
        BatchStatusReason? reason,
        int? providerResultCode,
        string detail,
        CancellationToken cancellationToken) =>
        InsertBatchEventAsync(
            connection, null, batchId, now, eventType, batchStatus, reason, providerResultCode, detail, cancellationToken);

    private static Task InsertBatchEventAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        long batchId,
        DateTime now,
        MessageBatchEventType eventType,
        BatchStatus? batchStatus,
        BatchStatusReason? reason,
        int? providerResultCode,
        string detail,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            DispatchSql.InsertBatchEvent,
            new
            {
                MessageBatchId = batchId,
                Now = now,
                EventType = (byte)eventType,
                BatchStatus = batchStatus is null ? null : (byte?)batchStatus.Value,
                BatchStatusReason = reason is null ? null : (byte?)reason.Value,
                ProviderResultCode = providerResultCode,
                Detail = detail,
            },
            transaction,
            cancellationToken: cancellationToken));

    private sealed record ClaimedBatch(
        long Id,
        short CustomerId,
        byte ProviderId,
        short SenderLineId,
        byte PreviousStatus,
        int DispatchAttemptCount);

    private sealed record QueuedMessage(
        long Id,
        string MobileNumber,
        decimal TotalCost,
        short CustomerId,
        string Body,
        byte Status,
        DateTime? AwaitingConfirmationSinceUtc,
        int ConfirmationLookupCount);

    private sealed record StatusCounts(int? Queued, int? Submitted, int? Rejected);

    private sealed record DispatchableMoney(short CustomerId, long MessageCount, decimal TotalCost);
}
