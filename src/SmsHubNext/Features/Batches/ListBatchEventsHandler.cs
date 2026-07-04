using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Batches;

/// <summary>Lists the operational timeline for a batch, oldest first.</summary>
public sealed class ListBatchEventsHandler
{
    private readonly Db _db;

    public ListBatchEventsHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<BatchEvent>>> Handle(long batchId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        bool exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            BatchesSql.Exists,
            new { Id = batchId },
            cancellationToken: cancellationToken));

        if (!exists)
            return Error.NotFound("batches.not_found", UserMessages.Batches.NotFound);

        IEnumerable<BatchEvent> rows = await connection.QueryAsync<BatchEvent>(new CommandDefinition(
            BatchesSql.ListEvents,
            new { BatchId = batchId },
            cancellationToken: cancellationToken));

        return Result.Success<IReadOnlyList<BatchEvent>>(rows.AsList());
    }
}
