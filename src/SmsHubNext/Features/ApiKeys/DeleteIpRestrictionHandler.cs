using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

public sealed class DeleteIpRestrictionHandler
{
    private readonly Db _db;

    public DeleteIpRestrictionHandler(Db db) => _db = db;

    public async Task<Result> Handle(
        int apiKeyId,
        int id,
        int deletedByApiKeyId,
        CancellationToken cancellationToken)
    {
        if (apiKeyId <= 0 || id <= 0)
            return Error.Validation("api_keys.invalid_id", UserMessages.ApiKeys.InvalidId);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            ApiKeyIpRestrictionsSql.SoftDelete,
            new { ApiKeyId = apiKeyId, Id = id, DeletedByApiKeyId = deletedByApiKeyId },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("api_keys.ip_restriction_not_found", UserMessages.ApiKeys.IpRestrictionNotFound)
            : Result.Success();
    }
}
