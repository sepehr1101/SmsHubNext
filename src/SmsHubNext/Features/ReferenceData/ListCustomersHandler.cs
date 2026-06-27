using Dapper;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

public sealed class ListCustomersHandler
{
    private readonly Db _db;

    public ListCustomersHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<Customer>>> Handle(CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Customer>(
            new CommandDefinition(CustomersSql.List, cancellationToken: cancellationToken));

        IReadOnlyList<Customer> customers = rows.AsList();
        return Result.Success(customers);
    }
}
