using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

public sealed class UpdateIpRestrictionHandler
{
    private readonly Db _db;

    public UpdateIpRestrictionHandler(Db db) => _db = db;

    public async Task<Result> Handle(
        int apiKeyId,
        int id,
        UpdateIpRestrictionRequest request,
        CancellationToken cancellationToken)
    {
        if (apiKeyId <= 0 || id <= 0)
            return Error.Validation("api_keys.invalid_id", UserMessages.ApiKeys.InvalidId);

        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            ApiKeyIpRestrictionsSql.Update,
            new { ApiKeyId = apiKeyId, Id = id, request.Cidr, request.Description },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("api_keys.ip_restriction_not_found", UserMessages.ApiKeys.IpRestrictionNotFound)
            : Result.Success();
    }
}
