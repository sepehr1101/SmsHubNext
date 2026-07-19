using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Tariffs;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Sms;
using SmsHubNext.Shared.Time;

namespace SmsHubNext.Features.Sending;

/// <summary>
/// Accepts a send request and persists it: resolves the sender line, prices each
/// message (frozen cost snapshot, README §6.3), then in one transaction debits the
/// prepaid balance (overspend-safe), writes the <c>MessageBatch</c> header, the money
/// ledger entry, and one <c>Message</c> (Queued) + <c>MessageBody</c> per recipient.
///
/// The per-recipient rows are the heavy part of an accept (up to <c>MaxMessages</c>), so the
/// <c>Message</c> and <c>MessageBody</c> rows are written with <c>SqlBulkCopy</c> enlisted in the
/// same transaction rather than row-by-row (the row mapping lives in <see cref="SendMessagesRowMapper"/>).
/// The messages land as <c>Queued</c>/<c>Pending</c> for the background dispatcher to pick up.
/// Pricing reuses the canonical tariff resolution (<see cref="TariffsSql.ResolveRate"/>) so quote
/// and send never drift.
///
/// <see cref="Handle"/> reads as the business flow — validate, resolve, price, persist — with each
/// step a small private method below.
/// </summary>
public sealed class SendMessagesHandler
{
    private readonly Db _db;
    private readonly TimeProvider _clock;

    public SendMessagesHandler(Db db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <param name="identity">
    /// The authenticated API key making the call, resolved from the request (header / API-key
    /// context) by the controller — never accepted in the request body. Used for per-call
    /// attribution and tenant ownership on <c>MessageBatch</c> and <c>Message</c>.
    /// </param>
    public async Task<Result<SendMessagesResponse>> Handle(
        SendMessagesRequest request,
        ApiKeyIdentity identity,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        Result references = await ValidateReferencesAsync(connection, request, identity, cancellationToken);
        if (references.IsFailure)
            return references.Error!;

        Result<SenderLineRow> senderLine = await ResolveSenderLineAsync(
            connection, request.SenderLine, identity.CustomerId, cancellationToken);
        if (senderLine.IsFailure)
            return senderLine.Error!;

        byte[] requestHash = SendMessagesRequestHasher.Hash(request);

        Result<SendMessagesResponse?> existing = await FindExistingBatchAsync(
            connection, request, identity.CustomerId, requestHash, cancellationToken);
        if (existing.IsFailure)
            return existing.Error!;
        if (existing.Value is not null)
            return existing.Value;

        Result<List<PricedMessage>> priced = await PriceMessagesAsync(connection, request, senderLine.Value, cancellationToken);
        if (priced.IsFailure)
            return priced.Error!;

        return await PersistAsync(connection, request, identity, senderLine.Value, priced.Value, requestHash, cancellationToken);
    }

    private static async Task<Result<SendMessagesResponse?>> FindExistingBatchAsync(
        SqlConnection connection,
        SendMessagesRequest request,
        short customerId,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        ExistingBatchRow? existing = await connection.QuerySingleOrDefaultAsync<ExistingBatchRow>(new CommandDefinition(
            SendingSql.GetExistingBatchByClientBatchId,
            new { CustomerId = customerId, request.ClientBatchId },
            cancellationToken: cancellationToken));

        if (existing is null)
            return Result.Success<SendMessagesResponse?>(null);

        if (existing.RequestHash is null || !existing.RequestHash.SequenceEqual(requestHash))
            return Error.Conflict(
                "sending.client_batch_payload_mismatch",
                UserMessages.Sending.ClientBatchPayloadMismatch);

        return new SendMessagesResponse(existing.BatchId, existing.AcceptedCount, IsDuplicate: true);
    }

    private static async Task<Result> ValidateReferencesAsync(
        SqlConnection connection,
        SendMessagesRequest request,
        ApiKeyIdentity identity,
        CancellationToken cancellationToken)
    {
        bool customerExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SendingSql.CustomerExists,
            new { identity.CustomerId },
            cancellationToken: cancellationToken));

        if (!customerExists)
            return Error.NotFound("sending.unknown_customer", UserMessages.Sending.UnknownCustomer);

        bool apiKeyBelongsToCustomer = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SendingSql.ApiKeyBelongsToCustomer,
            new { identity.ApiKeyId, identity.CustomerId },
            cancellationToken: cancellationToken));

        if (!apiKeyBelongsToCustomer)
            return Error.Validation("sending.api_key_customer_mismatch", UserMessages.Sending.ApiKeyCustomerMismatch);

        bool messageTypeExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SendingSql.MessageTypeExists,
            new { request.MessageTypeId },
            cancellationToken: cancellationToken));

        if (!messageTypeExists)
            return Error.NotFound("sending.unknown_message_type", UserMessages.Sending.UnknownMessageType);

        int[] geoSectionIds = request.Messages
            .Where(message => message.GeoSectionId is not null)
            .Select(message => message.GeoSectionId!.Value)
            .Distinct()
            .ToArray();

        if (geoSectionIds.Length == 0)
            return Result.Success();

        long existingGeoSections = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            SendingSql.CountExistingGeoSections,
            new { GeoSectionIds = geoSectionIds },
            cancellationToken: cancellationToken));

        if (existingGeoSections != geoSectionIds.Length)
            return Error.NotFound("sending.unknown_geo_section", UserMessages.Sending.UnknownGeoSection);

        return Result.Success();
    }

    // Resolve the requested sender line to its keys; it must exist and be active to send.
    private static async Task<Result<SenderLineRow>> ResolveSenderLineAsync(
        SqlConnection connection, string lineNumber, short customerId, CancellationToken cancellationToken)
    {
        SenderLineRow? senderLine = await connection.QuerySingleOrDefaultAsync<SenderLineRow>(new CommandDefinition(
            SendingSql.ResolveSenderLine,
            new { LineNumber = lineNumber },
            cancellationToken: cancellationToken));

        if (senderLine is null)
            return Error.NotFound("sending.unknown_sender_line", UserMessages.Sending.UnknownSenderLine);
        if (!senderLine.IsActive)
            return Error.Validation("sending.inactive_sender_line", UserMessages.Sending.InactiveSenderLine);
        if (!senderLine.IsSharedLine && senderLine.CustomerId is not null && senderLine.CustomerId != customerId)
            return Error.Validation("sending.sender_line_not_allowed", UserMessages.Sending.SenderLineNotAllowed);
        if (senderLine.ProviderAccountId is null)
            return Error.Validation("sending.provider_credentials_required", UserMessages.Sending.ProviderCredentialsRequired);
        if (senderLine.ProviderAccountIsActive != true)
            return Error.Validation("sending.provider_account_inactive", UserMessages.Sending.ProviderAccountInactive);
        if (senderLine.SecretLength is null or <= 0)
            return Error.Validation("sending.provider_secret_required", UserMessages.Sending.ProviderSecretRequired);

        return senderLine;
    }

    // Price every message up front (no transaction yet — tariff rates are stable reads). The resolved
    // price is frozen onto each message at persist time so later tariff changes never rewrite history.
    private static async Task<Result<List<PricedMessage>>> PriceMessagesAsync(
        SqlConnection connection, SendMessagesRequest request, SenderLineRow senderLine, CancellationToken cancellationToken)
    {
        List<PricedMessage> priced = new List<PricedMessage>(request.Messages.Count);
        Dictionary<RateLookupKey, RateRow> rateCache = new Dictionary<RateLookupKey, RateRow>();
        foreach (SendMessageItem item in request.Messages)
        {
            string normalizedRecipient = SendMessageItem.NormalizeRecipient(item.Recipient);
            SmsSegmentInfo segments = SmsPartCalculator.Calculate(item.Text);
            RateLookupKey key = new RateLookupKey(senderLine.ProviderId, request.MessageTypeId, segments.Encoding, segments.CharacterCount);

            if (!rateCache.TryGetValue(key, out RateRow? rate))
            {
                rate = await connection.QuerySingleOrDefaultAsync<RateRow>(new CommandDefinition(
                    TariffsSql.ResolveRate,
                    new
                    {
                        senderLine.ProviderId,
                        MessageTypeId = request.MessageTypeId,
                        Encoding = (byte)segments.Encoding,
                        segments.CharacterCount,
                    },
                    cancellationToken: cancellationToken));

                if (rate is null)
                    return await MissingRateErrorAsync(connection, request, senderLine, segments, cancellationToken);

                rateCache.Add(key, rate);
            }

            priced.Add(new PricedMessage(item, normalizedRecipient, segments, rate.TariffId, rate.PricePerSegment));
        }

        return priced;
    }

    private readonly record struct RateLookupKey(
        byte ProviderId,
        byte MessageTypeId,
        SmsEncoding Encoding,
        int CharacterCount);

    private static async Task<Error> MissingRateErrorAsync(
        SqlConnection connection,
        SendMessagesRequest request,
        SenderLineRow senderLine,
        SmsSegmentInfo segments,
        CancellationToken cancellationToken)
    {
        bool hasActiveTariff = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SendingSql.HasActiveTariff,
            new
            {
                senderLine.ProviderId,
                request.MessageTypeId,
                Encoding = (byte)segments.Encoding,
            },
            cancellationToken: cancellationToken));

        if (!hasActiveTariff)
        {
            return Error.NotFound(
                "sending.no_active_tariff",
                UserMessages.Sending.NoActiveTariff);
        }

        return Error.NotFound(
            "sending.no_tariff_rate_band",
            UserMessages.Sending.NoTariffRateBand);
    }

    // Persist atomically: debit the balance, then write the batch header, the ledger entry, and the
    // messages/bodies. Any unknown FK (customer/api key/message type/geo section) rolls the lot back.
    private async Task<Result<SendMessagesResponse>> PersistAsync(
        SqlConnection connection,
        SendMessagesRequest request,
        ApiKeyIdentity identity,
        SenderLineRow senderLine,
        IReadOnlyList<PricedMessage> priced,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        decimal totalCost = priced.Sum(p => p.TotalCost);
        int totalSegments = priced.Sum(p => p.Segments.SegmentCount);
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        string submitDateJalali = JalaliDate.FromUtc(nowUtc);

        using SqlTransaction transaction = connection.BeginTransaction();
        try
        {
            await DatabaseApplicationLock.AcquireFactoryResetSharedAsync(
                connection,
                transaction,
                cancellationToken);

            Result<decimal> balanceAfter = await DebitBalanceAsync(
                connection, transaction, identity.CustomerId, totalCost, cancellationToken);
            if (balanceAfter.IsFailure)
            {
                transaction.Rollback();
                return balanceAfter.Error!;
            }

            long batchId = await InsertBatchAsync(
                connection, transaction, request, identity, senderLine,
                priced.Count, totalSegments, totalCost, submitDateJalali, nowUtc, requestHash, cancellationToken);

            await InsertDebitLedgerAsync(
                connection, transaction, request, identity.CustomerId, totalCost, balanceAfter.Value, batchId, cancellationToken);

            await InsertAcceptedEventAsync(
                connection, transaction, batchId, priced.Count, totalSegments, totalCost, nowUtc, cancellationToken);

            await InsertMessagesAndBodiesAsync(
                connection, transaction, request, identity.CustomerId, senderLine, priced, batchId, submitDateJalali, nowUtc, cancellationToken);

            transaction.Commit();
            return new SendMessagesResponse(batchId, priced.Count);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict())
        {
            transaction.Rollback();
            return Error.Validation(
                "sending.unknown_reference",
                UserMessages.Sending.UnknownReference);
        }
        catch (SqlException ex) when (ex.IsUniqueViolation())
        {
            transaction.Rollback();
            Result<SendMessagesResponse?> existing = await FindExistingBatchAsync(
                connection, request, identity.CustomerId, requestHash, cancellationToken);
            if (existing.IsSuccess && existing.Value is not null)
                return existing.Value;

            return Error.Conflict("sending.duplicate_client_batch_id", UserMessages.Sending.DuplicateClientBatchId);
        }
    }

    // Overspend-safe debit in a single atomic statement (README §4.14): returns the post-debit balance,
    // or a validation failure when funds are insufficient (no row updated) so nothing is persisted.
    private static async Task<Result<decimal>> DebitBalanceAsync(
        SqlConnection connection, SqlTransaction transaction, short customerId, decimal amount, CancellationToken cancellationToken)
    {
        decimal? balanceAfter = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            SendingSql.DebitBalance,
            new { CustomerId = customerId, Amount = amount },
            transaction,
            cancellationToken: cancellationToken));

        if (balanceAfter is null)
            return Error.Validation("sending.insufficient_balance", UserMessages.Sending.InsufficientBalance);

        return balanceAfter.Value;
    }

    // Write the MessageBatch header (one row per API call) and return its server-assigned id.
    private static Task<long> InsertBatchAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SendMessagesRequest request,
        ApiKeyIdentity identity,
        SenderLineRow senderLine,
        int messageCount,
        int segmentCount,
        decimal totalCost,
        string submitDateJalali,
        DateTime nowUtc,
        byte[] requestHash,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<long>(new CommandDefinition(
            SendingSql.InsertBatch,
            new
            {
                SubmitDateJalali = submitDateJalali,
                NowUtc = nowUtc,
                identity.CustomerId,
                identity.ApiKeyId,
                SenderLineId = senderLine.Id,
                senderLine.ProviderId,
                request.ClientBatchId,
                RequestHash = requestHash,
                MessageCount = messageCount,
                SegmentCount = segmentCount,
                TotalCost = totalCost,
                Status = (byte)BatchStatus.Received,
            },
            transaction,
            cancellationToken: cancellationToken));

    // Append the signed money-ledger entry for the debit (debits are negative, README §4.15).
    private static Task InsertDebitLedgerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SendMessagesRequest request,
        short customerId,
        decimal totalCost,
        decimal balanceAfter,
        long batchId,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            SendingSql.InsertDebitLedger,
            new
            {
                CustomerId = customerId,
                Type = (byte)BalanceTransactionType.Debit,
                Amount = -totalCost,
                BalanceAfter = balanceAfter,
                MessageBatchId = batchId,
                Reference = request.ClientBatchId,
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task InsertAcceptedEventAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long batchId,
        int messageCount,
        int segmentCount,
        decimal totalCost,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            SendingSql.InsertBatchEvent,
            new
            {
                MessageBatchId = batchId,
                NowUtc = nowUtc,
                EventType = (byte)MessageBatchEventType.Accepted,
                BatchStatus = (byte)BatchStatus.Received,
                Detail = $"Batch accepted: {messageCount} message(s), {segmentCount} segment(s), total cost {totalCost:0.####} IRR.",
            },
            transaction,
            cancellationToken: cancellationToken));

    // Bulk-insert the heavy rows: the messages, then the server-assigned ids read back in insertion
    // order to key the 1:1 bodies, then the bodies. Both bulk copies enlist in this transaction (and
    // check FK constraints), so the whole accept stays atomic.
    private static async Task InsertMessagesAndBodiesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SendMessagesRequest request,
        short customerId,
        SenderLineRow senderLine,
        IReadOnlyList<PricedMessage> priced,
        long batchId,
        string submitDateJalali,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        using DataTable messageRows = SendMessagesRowMapper.BuildMessageRows(
            priced, request, customerId, batchId, senderLine.ProviderId, senderLine.Id, submitDateJalali, nowUtc);
        await connection.BulkInsertAsync(transaction, SendMessagesRowMapper.MessageTable, messageRows, cancellationToken);

        List<long> messageIds = (await connection.QueryAsync<long>(new CommandDefinition(
            SendingSql.SelectBatchMessageIds,
            new { MessageBatchId = batchId },
            transaction,
            cancellationToken: cancellationToken))).AsList();

        if (messageIds.Count != priced.Count)
            throw new InvalidOperationException(
                $"Bulk insert wrote {messageIds.Count} message(s) but {priced.Count} were expected.");

        using DataTable bodyRows = SendMessagesRowMapper.BuildBodyRows(messageIds, priced);
        await connection.BulkInsertAsync(transaction, SendMessagesRowMapper.MessageBodyTable, bodyRows, cancellationToken);
    }

    private sealed record RateRow(int TariffId, string Currency, decimal PricePerSegment);

    private sealed record ExistingBatchRow(long BatchId, int AcceptedCount, byte[]? RequestHash);
}
