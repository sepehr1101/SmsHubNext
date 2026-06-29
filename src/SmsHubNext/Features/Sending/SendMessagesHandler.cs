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
/// same transaction rather than row-by-row. The messages land as <c>Queued</c>/<c>Pending</c> for
/// the background dispatcher to pick up. Pricing reuses the canonical tariff resolution
/// (<see cref="TariffsSql.ResolveRate"/>) so quote and send never drift.
/// </summary>
public sealed class SendMessagesHandler
{
    private readonly Db _db;

    public SendMessagesHandler(Db db) => _db = db;

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

        // 1. Resolve the sender line (and the provider it belongs to).
        SenderLineRow? senderLine = await connection.QuerySingleOrDefaultAsync<SenderLineRow>(new CommandDefinition(
            SendingSql.ResolveSenderLine,
            new { LineNumber = request.SenderLine },
            cancellationToken: cancellationToken));

        if (senderLine is null)
            return Error.NotFound("sending.unknown_sender_line", "The sender line does not exist.");
        if (!senderLine.IsActive)
            return Error.Validation("sending.inactive_sender_line", "The sender line is not active.");

        // 2. Price every message up front (no transaction yet — tariff rates are stable reads).
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

        decimal totalCost = priced.Sum(p => p.TotalCost);
        int totalSegments = priced.Sum(p => p.Segments.SegmentCount);
        DateTime nowUtc = DateTime.UtcNow;
        string submitDateJalali = JalaliDate.FromUtc(nowUtc);

        // 3. Persist atomically: debit, then header, ledger, and the messages/bodies.
        using SqlTransaction transaction = connection.BeginTransaction();
        try
        {
            decimal? balanceAfter = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                SendingSql.DebitBalance,
                new { request.CustomerId, Amount = totalCost },
                transaction,
                cancellationToken: cancellationToken));

            // No row updated ⇒ insufficient funds (or the customer has no balance yet).
            // For now the whole request is rejected and nothing is persisted; recording a
            // Rejected MessageBatch for accounting visibility is an additive follow-up.
            if (balanceAfter is null)
            {
                transaction.Rollback();
                return Error.Validation(
                    "sending.insufficient_balance",
                    "The customer's prepaid balance is insufficient for this batch.");
            }

            long batchId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
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
                    MessageCount = priced.Count,
                    SegmentCount = totalSegments,
                    TotalCost = totalCost,
                    Status = (byte)BatchStatus.Received,
                },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                SendingSql.InsertDebitLedger,
                new
                {
                    request.CustomerId,
                    Type = (byte)BalanceTransactionType.Debit,
                    Amount = -totalCost, // signed: debits are negative (README §4.15)
                    BalanceAfter = balanceAfter.Value,
                    MessageBatchId = batchId,
                    Reference = request.ClientBatchId,
                },
                transaction,
                cancellationToken: cancellationToken));

            // Bulk-insert the heavy rows. First the messages, then read the server-assigned ids back
            // in insertion order to key the 1:1 bodies, then the bodies. Both bulk copies enlist in
            // this transaction (and check FK constraints), so the whole accept stays atomic.
            using DataTable messageRows = BuildMessageRows(priced, batchId, senderLine, request, submitDateJalali, nowUtc);
            await connection.BulkInsertAsync(transaction, "dbo.Message", messageRows, cancellationToken);

            List<long> messageIds = (await connection.QueryAsync<long>(new CommandDefinition(
                SendingSql.SelectBatchMessageIds,
                new { MessageBatchId = batchId },
                transaction,
                cancellationToken: cancellationToken))).AsList();

            if (messageIds.Count != priced.Count)
                throw new InvalidOperationException(
                    $"Bulk insert wrote {messageIds.Count} message(s) but {priced.Count} were expected.");

            using DataTable bodyRows = BuildBodyRows(messageIds, priced);
            await connection.BulkInsertAsync(transaction, "dbo.MessageBody", bodyRows, cancellationToken);

            transaction.Commit();
            return new SendMessagesResponse(batchId, priced.Count);
        }
        catch (SqlException ex) when (ex.Number == 547) // FK violation: unknown customer/api key/message type/geo section
        {
            transaction.Rollback();
            return Error.Validation(
                "sending.unknown_reference",
                "The customer, API key, message type, or geo section does not exist.");
        }
    }

    /// <summary>
    /// Builds the <c>Message</c> rows for bulk copy. Column order is independent of the destination
    /// (the helper maps by name and lets the server assign the identity <c>Id</c>); nullable columns
    /// carry <see cref="DBNull"/>. Column CLR types match the SQL types (TINYINT→byte, SMALLINT→short,
    /// INT→int, BIGINT→long, DECIMAL→decimal, CHAR/VARCHAR/NVARCHAR→string, DATETIME2→DateTime).
    /// </summary>
    private static DataTable BuildMessageRows(
        IReadOnlyList<PricedMessage> priced,
        long batchId,
        SenderLineRow senderLine,
        SendMessagesRequest request,
        string submitDateJalali,
        DateTime nowUtc)
    {
        DataTable table = new DataTable();
        table.Columns.Add("SubmitDateJalali", typeof(string));
        table.Columns.Add("MessageBatchId", typeof(long));
        table.Columns.Add("SubmittedAtUtc", typeof(DateTime));
        table.Columns.Add("CustomerId", typeof(short));
        table.Columns.Add("ProviderId", typeof(byte));
        table.Columns.Add("SenderLineId", typeof(short));
        table.Columns.Add("MessageTypeId", typeof(byte));
        table.Columns.Add("GeoSectionId", typeof(int));
        table.Columns.Add("MobileNumber", typeof(string));
        table.Columns.Add("ClientCorrelatedId", typeof(string));
        table.Columns.Add("BillId", typeof(string));
        table.Columns.Add("PayId", typeof(string));
        table.Columns.Add("Encoding", typeof(byte));
        table.Columns.Add("CharacterCount", typeof(short));
        table.Columns.Add("SegmentCount", typeof(byte));
        table.Columns.Add("TariffId", typeof(int));
        table.Columns.Add("UnitPrice", typeof(decimal));
        table.Columns.Add("TotalCost", typeof(decimal));
        table.Columns.Add("Status", typeof(byte));
        table.Columns.Add("DeliveryStatus", typeof(byte));

        foreach (PricedMessage message in priced)
        {
            table.Rows.Add(
                submitDateJalali,
                batchId,
                nowUtc,
                request.CustomerId,
                senderLine.ProviderId,
                senderLine.Id,
                request.MessageTypeId,
                (object?)message.Item.GeoSectionId ?? DBNull.Value,
                message.Item.Recipient,
                (object?)message.Item.ClientCorrelatedId ?? DBNull.Value,
                (object?)message.Item.BillId ?? DBNull.Value,
                (object?)message.Item.PayId ?? DBNull.Value,
                (byte)message.Segments.Encoding,
                (short)message.Segments.CharacterCount,
                (byte)message.Segments.SegmentCount,
                message.TariffId,
                message.UnitPrice,
                message.TotalCost,
                (byte)SendStatus.Queued,
                (byte)DeliveryStatus.Pending);
        }

        return table;
    }

    /// <summary>Builds the 1:1 <c>MessageBody</c> rows, pairing each read-back id with its body
    /// (both in insertion order — see <see cref="SendingSql.SelectBatchMessageIds"/>).</summary>
    private static DataTable BuildBodyRows(IReadOnlyList<long> messageIds, IReadOnlyList<PricedMessage> priced)
    {
        DataTable table = new DataTable();
        table.Columns.Add("Id", typeof(long));
        table.Columns.Add("Body", typeof(string));

        for (int i = 0; i < messageIds.Count; i++)
            table.Rows.Add(messageIds[i], priced[i].Item.Text);

        return table;
    }

    /// <summary>A message paired with its resolved cost snapshot, ready to persist.</summary>
    private sealed record PricedMessage(SendMessageItem Item, SmsSegmentInfo Segments, int TariffId, decimal UnitPrice)
    {
        public decimal TotalCost => UnitPrice * Segments.SegmentCount;
    }

    private sealed record SenderLineRow(short Id, byte ProviderId, bool IsActive);

    private sealed record RateRow(int TariffId, decimal PricePerSegment);
}
