using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
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

    /// <param name="apiKeyId">
    /// The authenticated API key making the call, resolved from the request (header / API-key
    /// context) by the controller — never accepted in the request body. Used for per-call
    /// attribution on <c>MessageBatch</c>.
    /// </param>
    public async Task<Result<SendMessagesResponse>> Handle(
        SendMessagesRequest request,
        int apiKeyId,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        Result<SenderLineRow> senderLine = await ResolveSenderLineAsync(connection, request.SenderLine, cancellationToken);
        if (senderLine.IsFailure)
            return senderLine.Error!;

        Result<List<PricedMessage>> priced = await PriceMessagesAsync(connection, request, senderLine.Value, cancellationToken);
        if (priced.IsFailure)
            return priced.Error!;

        return await PersistAsync(connection, request, apiKeyId, senderLine.Value, priced.Value, cancellationToken);
    }

    // Resolve the requested sender line to its keys; it must exist and be active to send.
    private static async Task<Result<SenderLineRow>> ResolveSenderLineAsync(
        SqlConnection connection, string lineNumber, CancellationToken cancellationToken)
    {
        SenderLineRow? senderLine = await connection.QuerySingleOrDefaultAsync<SenderLineRow>(new CommandDefinition(
            SendingSql.ResolveSenderLine,
            new { LineNumber = lineNumber },
            cancellationToken: cancellationToken));

        if (senderLine is null)
            return Error.NotFound("sending.unknown_sender_line", "The sender line does not exist.");
        if (!senderLine.IsActive)
            return Error.Validation("sending.inactive_sender_line", "The sender line is not active.");

        return senderLine;
    }

    // Price every message up front (no transaction yet — tariff rates are stable reads). The resolved
    // price is frozen onto each message at persist time so later tariff changes never rewrite history.
    private static async Task<Result<List<PricedMessage>>> PriceMessagesAsync(
        SqlConnection connection, SendMessagesRequest request, SenderLineRow senderLine, CancellationToken cancellationToken)
    {
        List<PricedMessage> priced = new List<PricedMessage>(request.Messages.Count);
        foreach (SendMessageItem item in request.Messages)
        {
            SmsSegmentInfo segments = SmsPartCalculator.Calculate(item.Text);

            RateRow? rate = await connection.QuerySingleOrDefaultAsync<RateRow>(new CommandDefinition(
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
                return Error.NotFound("sending.no_rate", "No active tariff rate matches a message in the batch.");

            priced.Add(new PricedMessage(item, segments, rate.TariffId, rate.PricePerSegment));
        }

        return priced;
    }

    // Persist atomically: debit the balance, then write the batch header, the ledger entry, and the
    // messages/bodies. Any unknown FK (customer/api key/message type/geo section) rolls the lot back.
    private async Task<Result<SendMessagesResponse>> PersistAsync(
        SqlConnection connection,
        SendMessagesRequest request,
        int apiKeyId,
        SenderLineRow senderLine,
        IReadOnlyList<PricedMessage> priced,
        CancellationToken cancellationToken)
    {
        decimal totalCost = priced.Sum(p => p.TotalCost);
        int totalSegments = priced.Sum(p => p.Segments.SegmentCount);
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        string submitDateJalali = JalaliDate.FromUtc(nowUtc);

        using SqlTransaction transaction = connection.BeginTransaction();
        try
        {
            Result<decimal> balanceAfter = await DebitBalanceAsync(
                connection, transaction, request.CustomerId, totalCost, cancellationToken);
            if (balanceAfter.IsFailure)
            {
                transaction.Rollback();
                return balanceAfter.Error!;
            }

            long batchId = await InsertBatchAsync(
                connection, transaction, request, apiKeyId, senderLine,
                priced.Count, totalSegments, totalCost, submitDateJalali, nowUtc, cancellationToken);

            await InsertDebitLedgerAsync(
                connection, transaction, request, totalCost, balanceAfter.Value, batchId, cancellationToken);

            await InsertMessagesAndBodiesAsync(
                connection, transaction, request, senderLine, priced, batchId, submitDateJalali, nowUtc, cancellationToken);

            transaction.Commit();
            return new SendMessagesResponse(batchId, priced.Count);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict())
        {
            transaction.Rollback();
            return Error.Validation(
                "sending.unknown_reference",
                "The customer, API key, message type, or geo section does not exist.");
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
            return Error.Validation("sending.insufficient_balance", "The customer's prepaid balance is insufficient for this batch.");

        return balanceAfter.Value;
    }

    // Write the MessageBatch header (one row per API call) and return its server-assigned id.
    private static Task<long> InsertBatchAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SendMessagesRequest request,
        int apiKeyId,
        SenderLineRow senderLine,
        int messageCount,
        int segmentCount,
        decimal totalCost,
        string submitDateJalali,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<long>(new CommandDefinition(
            SendingSql.InsertBatch,
            new
            {
                SubmitDateJalali = submitDateJalali,
                NowUtc = nowUtc,
                request.CustomerId,
                ApiKeyId = apiKeyId,
                SenderLineId = senderLine.Id,
                senderLine.ProviderId,
                request.ClientBatchId,
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
        decimal totalCost,
        decimal balanceAfter,
        long batchId,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            SendingSql.InsertDebitLedger,
            new
            {
                request.CustomerId,
                Type = (byte)BalanceTransactionType.Debit,
                Amount = -totalCost,
                BalanceAfter = balanceAfter,
                MessageBatchId = batchId,
                Reference = request.ClientBatchId,
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
        SenderLineRow senderLine,
        IReadOnlyList<PricedMessage> priced,
        long batchId,
        string submitDateJalali,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        using DataTable messageRows = SendMessagesRowMapper.BuildMessageRows(
            priced, request, batchId, senderLine.ProviderId, senderLine.Id, submitDateJalali, nowUtc);
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

    private sealed record SenderLineRow(short Id, byte ProviderId, bool IsActive);

    private sealed record RateRow(int TariffId, string Currency, decimal PricePerSegment);
}
