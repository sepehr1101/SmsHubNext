using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ProviderAccounts;

public sealed class ListProviderAccountsHandler
{
    private readonly Db _db;

    public ListProviderAccountsHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<ProviderAccount>>> Handle(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<ProviderAccountRow> rows = await connection.QueryAsync<ProviderAccountRow>(
            new CommandDefinition(ProviderAccountsSql.List, cancellationToken: cancellationToken));

        IReadOnlyList<ProviderAccount> accounts = rows.Select(row => row.ToModel()).ToList();
        return Result.Success(accounts);
    }
}
