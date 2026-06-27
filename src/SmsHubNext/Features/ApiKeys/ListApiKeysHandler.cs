using Dapper;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

public sealed class ListApiKeysHandler
{
    private readonly Db _db;

    public ListApiKeysHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<ApiKey>>> Handle(short customerId, CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<ApiKey>(new CommandDefinition(
            ApiKeysSql.ListByCustomer,
            new { CustomerId = customerId },
            cancellationToken: cancellationToken));

        IReadOnlyList<ApiKey> keys = rows.AsList();
        return Result.Success(keys);
    }
}
