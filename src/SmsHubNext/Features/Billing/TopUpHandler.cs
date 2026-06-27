using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Billing;

/// <summary>
/// Credits a customer's prepaid balance and records a ledger entry, atomically.
/// The balance column and the ledger must always reconcile (README §4.15).
/// </summary>
public sealed class TopUpHandler
{
    private readonly Db _db;

    public TopUpHandler(Db db) => _db = db;

    public async Task<Result<TopUpResponse>> Handle(TopUpRequest request, CancellationToken cancellationToken)
    {
        var validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using var connection = await _db.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            var updatedBalance = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                BalancesSql.CreditBalance,
                new { request.CustomerId, request.Amount },
                transaction,
                cancellationToken: cancellationToken));

            decimal balanceAfter;
            if (updatedBalance is null)
            {
                // First credit for this customer — create the balance row (FK checks the customer exists).
                await connection.ExecuteAsync(new CommandDefinition(
                    BalancesSql.InsertBalance,
                    new { request.CustomerId, request.Amount },
                    transaction,
                    cancellationToken: cancellationToken));
                balanceAfter = request.Amount;
            }
            else
            {
                balanceAfter = updatedBalance.Value;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                BalancesSql.InsertLedger,
                new
                {
                    request.CustomerId,
                    Type = (byte)BalanceTransactionType.TopUp,
                    request.Amount,
                    BalanceAfter = balanceAfter,
                    request.Reference,
                },
                transaction,
                cancellationToken: cancellationToken));

            transaction.Commit();
            return new TopUpResponse(request.CustomerId, balanceAfter);
        }
        catch (SqlException ex) when (ex.Number == 547) // FK violation: unknown customer
        {
            transaction.Rollback();
            return Error.Validation("balances.unknown_customer", "The customer does not exist.");
        }
    }
}
