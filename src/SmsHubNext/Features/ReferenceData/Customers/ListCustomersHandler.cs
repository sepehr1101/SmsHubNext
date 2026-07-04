using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Customers;

public sealed class ListCustomersHandler
{
    private readonly Db _db;

    public ListCustomersHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<Customer>>> Handle(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<Customer> rows = await connection.QueryAsync<Customer>(
            new CommandDefinition(CustomersSql.List, cancellationToken: cancellationToken));

        IReadOnlyList<Customer> customers = rows.AsList();
        return Result.Success(customers);
    }
}
