using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Tariffs;

public sealed class DeleteTariffHandler
{
    private readonly Db _db;

    public DeleteTariffHandler(Db db) => _db = db;

    public async Task<Result> Handle(int id, int deletedByApiKeyId, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("tariffs.invalid_id", UserMessages.Tariffs.InvalidId);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            TariffsSql.SoftDelete,
            new { Id = id, DeletedByApiKeyId = deletedByApiKeyId },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("tariffs.not_found", UserMessages.Tariffs.NotFound)
            : Result.Success();
    }
}
