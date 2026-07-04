using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

public sealed class AddIpRestrictionHandler
{
    private readonly Db _db;

    public AddIpRestrictionHandler(Db db) => _db = db;

    public async Task<Result<ApiKeyIpRestriction>> Handle(
        int apiKeyId,
        AddIpRestrictionRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        try
        {
            int id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                ApiKeyIpRestrictionsSql.Insert,
                new { ApiKeyId = apiKeyId, request.Cidr, request.Description },
                cancellationToken: cancellationToken));

            return new ApiKeyIpRestriction(id, apiKeyId, request.Cidr, request.Description);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict()) // unknown API key
        {
            return Error.Validation("api_keys.unknown_key", UserMessages.ApiKeys.UnknownKey);
        }
    }
}
