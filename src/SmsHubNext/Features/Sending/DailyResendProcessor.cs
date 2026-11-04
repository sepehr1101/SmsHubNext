using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Tariffs;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Sending;

public sealed class DailyResendProcessor(Db db, ResendMessageHandler resend, TimeProvider clock)
{
    public async Task<bool> ProcessNext(CancellationToken token)
    {
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        DailyOperation? operation = await connection.QuerySingleOrDefaultAsync<DailyOperation>(
            new CommandDefinition(DailyResendSql.NextOperation, cancellationToken: token));
        if (operation is null) return false;
        string resource = DailyResendHandler.Resource(operation.SubmitDateJalali);
        if (!await ResendLock.TryAcquire(connection, resource, 0, token)) return false;
        try
        {
            operation = await connection.QuerySingleAsync<DailyOperation>(new CommandDefinition("""
                SELECT Id,SubmitDateJalali,CutoffMessageId,State,ConfirmedByApiKeyId
                FROM dbo.DailyResendOperation WHERE Id=@Id;
                """, new { operation.Id }, cancellationToken: token));
            if (operation.State == DailyResendState.Preparing) await Prepare(connection, operation, token);
            else if (operation.State == DailyResendState.Running) await Execute(connection, operation, token);
            return true;
        }
        finally { await ResendLock.Release(connection, resource); }
    }

    private async Task Prepare(SqlConnection connection, DailyOperation operation, CancellationToken token)
    {
        // Only narrow metadata is buffered, never the day's message bodies.
        List<DailyCandidate> candidates = (await connection.QueryAsync<DailyCandidate>(new CommandDefinition(
            DailyResendSql.Candidates, new { Day = operation.SubmitDateJalali, Cutoff = operation.CutoffMessageId },
            commandTimeout: 120, cancellationToken: token))).AsList();
        Dictionary<(byte, byte, byte, int), decimal?> rates = new();
        foreach (DailyCandidate[] chunk in candidates.Chunk(500))
        {
            List<DailyPlannedItem> planned = new(chunk.Length);
            foreach (DailyCandidate candidate in chunk)
                planned.Add(await Price(connection, operation.Id, candidate, rates, token));
            using SqlTransaction transaction = connection.BeginTransaction();
            using DataTable rows = DailyResendRowMapper.Build(planned);
            await connection.BulkInsertAsync(transaction, DailyResendRowMapper.Table, rows, token);
            transaction.Commit();
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.DailyResendOperation SET State=CASE WHEN EXISTS (
                SELECT 1 FROM dbo.DailyResendItem WHERE OperationId=@Id AND State=0) THEN 1 ELSE 3 END
            WHERE Id=@Id AND State=0;
            """, new { operation.Id }, cancellationToken: token));
    }

    private static async Task<DailyPlannedItem> Price(SqlConnection connection, Guid operationId, DailyCandidate candidate,
        Dictionary<(byte, byte, byte, int), decimal?> rates, CancellationToken token)
    {
        string? skip = candidate.SkipCode;
        decimal? cost = null;
        if (skip is null)
        {
            (byte, byte, byte, int) key = (candidate.ProviderId, candidate.MessageTypeId, candidate.Encoding, candidate.CharacterCount);
            if (!rates.TryGetValue(key, out decimal? rate))
            {
                DailyRate? resolved = await connection.QuerySingleOrDefaultAsync<DailyRate>(
                    new CommandDefinition(TariffsSql.ResolveRate, candidate, cancellationToken: token));
                rate = resolved?.PricePerSegment;
                rates.Add(key, rate);
            }
            if (rate is null) skip = "sending.no_active_tariff";
            else cost = rate.Value * candidate.SegmentCount;
        }
        return new(operationId, candidate.SourceMessageId, Guid.NewGuid(), cost, skip is null ? (byte)0 : (byte)2, skip);
    }

    private async Task Execute(SqlConnection connection, DailyOperation operation, CancellationToken token)
    {
        List<DailyPendingItem> items = (await connection.QueryAsync<DailyPendingItem>(new CommandDefinition("""
            SELECT TOP (25) Id,SourceMessageId,RequestId,EstimatedCost FROM dbo.DailyResendItem
            WHERE OperationId=@Id AND State=0 ORDER BY Id;
            """, new { operation.Id }, cancellationToken: token))).AsList();
        foreach (DailyPendingItem item in items)
        {
            ApiKeyIdentity? actor = await connection.QuerySingleOrDefaultAsync<ApiKeyIdentity>(new CommandDefinition(
                DailyResendSql.Identity, new { Id = operation.ConfirmedByApiKeyId, Now = clock.GetUtcNow().UtcDateTime }, cancellationToken: token));
            if (actor is null)
            {
                await connection.ExecuteAsync(new CommandDefinition("UPDATE dbo.DailyResendOperation SET State=4 WHERE Id=@Id;",
                    new { operation.Id }, cancellationToken: token));
                return;
            }
            Result<ResendMessageResponse> result = await resend.HandleDaily(item.SourceMessageId, item.RequestId,
                operation.SubmitDateJalali, item.EstimatedCost, actor, token);
            if (result.IsFailure && result.Error!.Code == "sending.resend_busy") return;
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.DailyResendItem SET State=@State,SkipCode=@SkipCode,NewMessageId=@MessageId,NewBatchId=@BatchId
                WHERE Id=@Id AND State=0;
                """, new { item.Id, State = result.IsSuccess ? (byte)1 : (byte)2, SkipCode = result.Error?.Code,
                    MessageId = result.IsSuccess ? (long?)result.Value.MessageId : null,
                    BatchId = result.IsSuccess ? (long?)result.Value.BatchId : null }, cancellationToken: token));
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.DailyResendOperation SET State=3,FinishedAtUtc=@Now WHERE Id=@Id AND State=2
              AND NOT EXISTS (SELECT 1 FROM dbo.DailyResendItem WHERE OperationId=@Id AND State=0);
            """, new { operation.Id, Now = clock.GetUtcNow().UtcDateTime }, cancellationToken: token));
    }

    private sealed record DailyOperation(Guid Id, string SubmitDateJalali, long CutoffMessageId, DailyResendState State, int? ConfirmedByApiKeyId);
    private sealed record DailyCandidate(long SourceMessageId, byte ProviderId, byte MessageTypeId, byte Encoding, int CharacterCount, int SegmentCount, string? SkipCode);
    private sealed record DailyRate(int TariffId, string Currency, decimal PricePerSegment);
    private sealed record DailyPendingItem(long Id, long SourceMessageId, Guid RequestId, decimal EstimatedCost);
}
