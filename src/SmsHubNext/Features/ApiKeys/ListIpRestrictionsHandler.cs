using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

public sealed class ListIpRestrictionsHandler
{
    private readonly Db _db;

    public ListIpRestrictionsHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<ApiKeyIpRestriction>>> Handle(int apiKeyId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<ApiKeyIpRestriction> rows = await connection.QueryAsync<ApiKeyIpRestriction>(new CommandDefinition(
            ApiKeyIpRestrictionsSql.ListByApiKey,
            new { ApiKeyId = apiKeyId },
            cancellationToken: cancellationToken));

        IReadOnlyList<ApiKeyIpRestriction> restrictions = rows.AsList();
        return Result.Success(restrictions);
    }
}
