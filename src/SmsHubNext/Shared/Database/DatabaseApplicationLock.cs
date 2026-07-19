using Dapper;
using Microsoft.Data.SqlClient;

namespace SmsHubNext.Shared.Database;

/// <summary>
/// Coordinates message acceptance with the destructive factory-reset operation across
/// every application instance connected to the same database.
/// </summary>
public static class DatabaseApplicationLock
{
    private const string FactoryResetResource = "SmsHubNext.FactoryReset";
    private const int LockTimeoutMilliseconds = 10_000;

    private const string AcquireSql =
        """
        DECLARE @Result INT;

        EXEC @Result = sys.sp_getapplock
            @Resource = @Resource,
            @LockMode = @LockMode,
            @LockOwner = 'Transaction',
            @LockTimeout = @LockTimeoutMilliseconds;

        SELECT @Result;
        """;

    public static Task AcquireFactoryResetSharedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken) =>
        AcquireAsync(connection, transaction, "Shared", cancellationToken);

    public static Task AcquireFactoryResetExclusiveAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken) =>
        AcquireAsync(connection, transaction, "Exclusive", cancellationToken);

    private static async Task AcquireAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string lockMode,
        CancellationToken cancellationToken)
    {
        int result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            AcquireSql,
            new
            {
                Resource = FactoryResetResource,
                LockMode = lockMode,
                LockTimeoutMilliseconds,
            },
            transaction,
            cancellationToken: cancellationToken));

        if (result < 0)
            throw new TimeoutException("The database factory-reset lock could not be acquired in time.");
    }
}
