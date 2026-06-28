using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Billing;

public sealed class ListTransactionsHandler
{
    private readonly Db _db;

    public ListTransactionsHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<BalanceTransaction>>> Handle(short customerId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<BalanceTransaction> rows = await connection.QueryAsync<BalanceTransaction>(new CommandDefinition(
            BalancesSql.ListTransactions,
            new { CustomerId = customerId },
            cancellationToken: cancellationToken));

        IReadOnlyList<BalanceTransaction> transactions = rows.AsList();
        return Result.Success(transactions);
    }
}
