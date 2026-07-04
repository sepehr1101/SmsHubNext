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
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        string? reference = NormalizeReference(request.Reference);

        if (reference is not null)
        {
            TopUpResponse? existing = await FindExistingTopUpAsync(connection, request, reference, cancellationToken);
            if (existing is not null)
                return existing;
        }

        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            decimal? updatedBalance = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
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
                    Reference = reference,
                },
                transaction,
                cancellationToken: cancellationToken));

            transaction.Commit();
            return new TopUpResponse(request.CustomerId, balanceAfter);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict()) // unknown customer
        {
            transaction.Rollback();
            return Error.Validation("balances.unknown_customer", "The customer does not exist.");
        }
        catch (SqlException ex) when (ex.IsUniqueViolation())
        {
            transaction.Rollback();
            TopUpResponse? existing = reference is null
                ? null
                : await FindExistingTopUpAsync(connection, request, reference, cancellationToken);
            if (existing is not null)
                return existing;

            return Error.Conflict("balances.duplicate_reference", "A top-up with this reference already exists.");
        }
    }

    private static async Task<TopUpResponse?> FindExistingTopUpAsync(
        SqlConnection connection,
        TopUpRequest request,
        string reference,
        CancellationToken cancellationToken)
    {
        decimal? balanceAfter = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            BalancesSql.GetTopUpByReference,
            new
            {
                request.CustomerId,
                Type = (byte)BalanceTransactionType.TopUp,
                Reference = reference,
            },
            cancellationToken: cancellationToken));

        return balanceAfter is null
            ? null
            : new TopUpResponse(request.CustomerId, balanceAfter.Value, IsDuplicate: true);
    }

    private static string? NormalizeReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        return reference.Trim();
    }
}
