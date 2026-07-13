using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

public sealed class RevokeApiKeyHandler
{
    private readonly Db _db;

    public RevokeApiKeyHandler(Db db) => _db = db;

    public async Task<Result> Handle(int id, int revokedByApiKeyId, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("api_keys.invalid_id", UserMessages.ApiKeys.InvalidId);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            ApiKeysSql.Revoke,
            new { Id = id, RevokedByApiKeyId = revokedByApiKeyId },
            cancellationToken: cancellationToken));

        if (affectedRows > 0)
            return Result.Success();

        bool exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            ApiKeysSql.Exists,
            new { Id = id },
            cancellationToken: cancellationToken));

        return exists
            ? Error.Conflict("api_keys.already_revoked", UserMessages.ApiKeys.AlreadyRevoked)
            : Error.NotFound("api_keys.not_found", UserMessages.ApiKeys.NotFound);
    }
}
