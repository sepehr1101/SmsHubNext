using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Providers;

public sealed class DeleteProviderHandler
{
    private readonly Db _db;

    public DeleteProviderHandler(Db db) => _db = db;

    public async Task<Result> Handle(byte id, int deletedByApiKeyId, CancellationToken cancellationToken)
    {
        if (id == 0)
            return Error.Validation("providers.invalid_id", UserMessages.ReferenceData.InvalidProvider);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            ProvidersSql.SoftDelete,
            new { Id = id, DeletedByApiKeyId = deletedByApiKeyId },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("providers.not_found", UserMessages.ReferenceData.ProviderNotFound)
            : Result.Success();
    }
}
