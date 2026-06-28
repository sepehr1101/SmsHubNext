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
/// Dispatch to a provider is a later increment (Phase 1+, Magfa): the messages land as
/// <c>Queued</c>/<c>Pending</c> for a background worker to pick up. Pricing reuses the
/// canonical tariff resolution (<see cref="TariffsSql.ResolveRate"/>) so quote and send
/// never drift.
/// </summary>
public sealed class SendMessagesHandler
{
    private readonly Db _db;

    public SendMessagesHandler(Db db) => _db = db;

    public async Task<Result<SendMessagesResponse>> Handle(
        SendMessagesRequest request,
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
                    request.ApiKeyId,
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

            foreach (PricedMessage message in priced)
            {
                long messageId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    SendingSql.InsertMessage,
                    new
                    {
                        SubmitDateJalali = submitDateJalali,
                        MessageBatchId = batchId,
                        NowUtc = nowUtc,
                        request.CustomerId,
                        senderLine.ProviderId,
                        SenderLineId = senderLine.Id,
                        request.MessageTypeId,
                        message.Item.GeoSectionId,
                        MobileNumber = message.Item.Recipient,
                        message.Item.ClientCorrelatedId,
                        message.Item.BillId,
                        message.Item.PayId,
                        Encoding = (byte)message.Segments.Encoding,
                        message.Segments.CharacterCount,
                        SegmentCount = (byte)message.Segments.SegmentCount,
                        message.TariffId,
                        message.UnitPrice,
                        message.TotalCost,
                        Status = (byte)SendStatus.Queued,
                        DeliveryStatus = (byte)DeliveryStatus.Pending,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                await connection.ExecuteAsync(new CommandDefinition(
                    SendingSql.InsertBody,
                    new { Id = messageId, Body = message.Item.Text },
                    transaction,
                    cancellationToken: cancellationToken));
            }

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

    /// <summary>A message paired with its resolved cost snapshot, ready to persist.</summary>
    private sealed record PricedMessage(SendMessageItem Item, SmsSegmentInfo Segments, int TariffId, decimal UnitPrice)
    {
        public decimal TotalCost => UnitPrice * Segments.SegmentCount;
    }

    private sealed record SenderLineRow(short Id, byte ProviderId, bool IsActive);

    private sealed record RateRow(int TariffId, decimal PricePerSegment);
}
