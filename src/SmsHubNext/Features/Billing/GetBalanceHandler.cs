using Dapper;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Billing;

public sealed class GetBalanceHandler
{
    private readonly Db _db;

    public GetBalanceHandler(Db db) => _db = db;

    public async Task<Result<CustomerBalance>> Handle(short customerId, CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        var balance = await connection.QuerySingleOrDefaultAsync<CustomerBalance>(new CommandDefinition(
            BalancesSql.GetByCustomer,
            new { CustomerId = customerId },
            cancellationToken: cancellationToken));

        // No balance row yet means the customer has never been credited — report zero.
        return balance ?? new CustomerBalance(customerId, 0m, DateTime.UtcNow);
    }
}
