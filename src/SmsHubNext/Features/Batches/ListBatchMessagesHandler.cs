using Dapper;
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

    public async Task<Result<IReadOnlyList<BatchMessage>>> Handle(long batchId, CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<BatchMessage>(new CommandDefinition(
            BatchesSql.ListMessages,
            new { BatchId = batchId },
            cancellationToken: cancellationToken));

        IReadOnlyList<BatchMessage> messages = rows.AsList();
        return messages.Count == 0
            ? Error.NotFound("batches.not_found", "The batch does not exist.")
            : Result.Success(messages);
    }
}
