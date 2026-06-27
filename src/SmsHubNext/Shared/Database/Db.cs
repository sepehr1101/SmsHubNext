using Microsoft.Data.SqlClient;

namespace SmsHubNext.Shared.Database;

/// <summary>
/// Opens connections to the SmsHubNext SQL Server database.
///
/// Concrete, with no interface: there is one implementation and we never swap
/// SQL Server, so a seam here would be pure ceremony (ARCHITECTURE.md §5,
/// ADR-003). Features open a connection, run their own Dapper SQL, and manage
/// any transaction inline. Integration tests use a real database rather than
/// mocking this.
/// </summary>
public sealed class Db
{
    /// <summary>Name of the connection string in configuration.</summary>
    public const string ConnectionStringName = "SmsHubNext";

    private readonly string _connectionString;

    public Db(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A connection string is required.", nameof(connectionString));

        _connectionString = connectionString;
    }

    /// <summary>A new, unopened connection. Dapper opens it on first use.</summary>
    public SqlConnection CreateConnection() => new(_connectionString);

    /// <summary>
    /// A new, already-open connection — convenient when the caller needs an
    /// explicit transaction (<c>conn.BeginTransaction()</c>).
    /// </summary>
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
