using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Batches;

/// <summary>Reads one batch header by id for status polling (README §4.13).</summary>
public sealed class GetBatchHandler
{
    private readonly Db _db;

    public GetBatchHandler(Db db) => _db = db;

    public async Task<Result<Batch>> Handle(long batchId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        Batch? batch = await connection.QuerySingleOrDefaultAsync<Batch>(new CommandDefinition(
            BatchesSql.GetById,
            new { Id = batchId },
            cancellationToken: cancellationToken));

        return batch is null
            ? Error.NotFound("batches.not_found", "The batch does not exist.")
            : batch;
    }
}
