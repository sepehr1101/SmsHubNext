using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

/// <summary>Reads the configured SMS providers.</summary>
public sealed class ListProvidersHandler
{
    private readonly Db _db;

    public ListProvidersHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<Provider>>> Handle(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<Provider> rows = await connection.QueryAsync<Provider>(
            new CommandDefinition(ProvidersSql.List, cancellationToken: cancellationToken));

        IReadOnlyList<Provider> providers = rows.AsList();
        return Result.Success(providers);
    }
}
