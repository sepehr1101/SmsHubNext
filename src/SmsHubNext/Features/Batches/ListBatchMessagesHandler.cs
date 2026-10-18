using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Batches;

/// <summary>
/// Lists the messages in a batch with their current send/delivery status. Every batch
/// has at least one message (the send path enforces it), so an empty result means the
/// batch id is unknown — reported as 404 rather than an empty list.
/// </summary>
public sealed class ListBatchMessagesHandler
{
    private readonly Db _db;

    public ListBatchMessagesHandler(Db db) => _db = db;

    public async Task<Result<BatchMessagesPage>> HandlePage(long batchId, long? afterId, CancellationToken token)
    {
        if (batchId <= 0 || afterId <= 0)
            return Error.Validation("batches.invalid_id", "شناسهٔ دسته یا صفحه معتبر نیست.");
        await using SqlConnection connection = await _db.OpenConnectionAsync(token);
        string? date = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            BatchesSql.GetPartition, new { BatchId = batchId }, cancellationToken: token));
        if (date is null)
            return Error.NotFound("batches.not_found", UserMessages.Batches.NotFound);
        List<BatchMessage> rows = (await connection.QueryAsync<BatchMessage>(new CommandDefinition(
            BatchesSql.ListMessagesPage, new
            {
                BatchId = batchId, AfterId = afterId, Take = BrowseBatchesHandler.PageSize + 1,
                SubmitDateJalali = new DbString { Value = date, IsAnsi = true, Length = 10 },
            },
            cancellationToken: token))).AsList();
        bool hasMore = rows.Count > BrowseBatchesHandler.PageSize;
        if (hasMore) rows.RemoveAt(BrowseBatchesHandler.PageSize);
        return new BatchMessagesPage(rows, hasMore ? rows[^1].Id : null);
    }

    public async Task<Result<IReadOnlyList<BatchMessage>>> Handle(long batchId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<BatchMessage> rows = await connection.QueryAsync<BatchMessage>(new CommandDefinition(
            BatchesSql.ListMessages,
            new { BatchId = batchId },
            cancellationToken: cancellationToken));

        IReadOnlyList<BatchMessage> messages = rows.AsList();
        return messages.Count == 0
            ? Error.NotFound("batches.not_found", UserMessages.Batches.NotFound)
            : Result.Success(messages);
    }
}
