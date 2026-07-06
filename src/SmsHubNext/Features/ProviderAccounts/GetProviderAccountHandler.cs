using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ProviderAccounts;

public sealed class GetProviderAccountHandler
{
    private readonly Db _db;

    public GetProviderAccountHandler(Db db) => _db = db;

    public async Task<Result<ProviderAccount>> Handle(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("provider_accounts.invalid_id", UserMessages.ProviderAccounts.InvalidId);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        ProviderAccountRow? row = await connection.QuerySingleOrDefaultAsync<ProviderAccountRow>(
            new CommandDefinition(ProviderAccountsSql.Get, new { Id = id }, cancellationToken: cancellationToken));

        if (row is null)
            return Error.NotFound("provider_accounts.not_found", UserMessages.ProviderAccounts.NotFound);

        return row.ToModel();
    }
}
