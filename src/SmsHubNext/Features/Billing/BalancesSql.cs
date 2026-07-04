namespace SmsHubNext.Features.Billing;

internal static class BalancesSql
{
    public const string GetByCustomer =
        "SELECT CustomerId, Balance, UpdatedAtUtc FROM dbo.CustomerBalance WHERE CustomerId = @CustomerId;";

    // Atomic credit; OUTPUT returns the new balance, or no rows if the customer has no balance yet.
    public const string CreditBalance =
        """
        UPDATE dbo.CustomerBalance
        SET Balance += @Amount, UpdatedAtUtc = SYSUTCDATETIME()
        OUTPUT INSERTED.Balance
        WHERE CustomerId = @CustomerId;
        """;

    public const string InsertBalance =
        "INSERT INTO dbo.CustomerBalance (CustomerId, Balance) VALUES (@CustomerId, @Amount);";

    public const string InsertLedger =
        """
        INSERT INTO dbo.BalanceTransaction (CustomerId, Type, Amount, BalanceAfter, Reference)
        VALUES (@CustomerId, @Type, @Amount, @BalanceAfter, @Reference);
        """;

    public const string GetTopUpByReference =
        """
        SELECT BalanceAfter
        FROM dbo.BalanceTransaction
        WHERE CustomerId = @CustomerId
          AND Type = @Type
          AND Reference = @Reference;
        """;

    public const string ListTransactions =
        """
        SELECT Id, CustomerId, Type, Amount, BalanceAfter, MessageBatchId, Reference, CreatedAtUtc
        FROM dbo.BalanceTransaction
        WHERE CustomerId = @CustomerId
        ORDER BY Id;
        """;
}
