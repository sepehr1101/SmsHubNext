using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Billing;

public sealed class GetBalanceHandler
{
    private readonly Db _db;
    private readonly TimeProvider _clock;

    public GetBalanceHandler(Db db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<CustomerBalance>> Handle(short customerId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        CustomerBalance? balance = await connection.QuerySingleOrDefaultAsync<CustomerBalance>(new CommandDefinition(
            BalancesSql.GetByCustomer,
            new { CustomerId = customerId },
            cancellationToken: cancellationToken));

        // No balance row yet means the customer has never been credited — report zero.
        return balance ?? new CustomerBalance(customerId, 0m, _clock.GetUtcNow().UtcDateTime);
    }
}
