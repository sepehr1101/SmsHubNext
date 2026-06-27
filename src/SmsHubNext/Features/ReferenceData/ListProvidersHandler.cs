using Dapper;
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
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Provider>(
            new CommandDefinition(ProvidersSql.List, cancellationToken: cancellationToken));

        IReadOnlyList<Provider> providers = rows.AsList();
        return Result.Success(providers);
    }
}
