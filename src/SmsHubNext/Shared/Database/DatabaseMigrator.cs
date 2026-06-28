using DbUp.Engine;
using DbUp;

namespace SmsHubNext.Shared.Database;

/// <summary>
/// Applies the forward-only SQL migrations embedded in this assembly (DbUp,
/// ARCHITECTURE.md §10). Creates the database if it is missing and runs any
/// scripts not yet recorded in the journal. Idempotent — already-applied scripts
/// are skipped, so it is safe to run on every startup.
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly string _connectionString;

    public DatabaseMigrator(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A connection string is required.", nameof(connectionString));

        _connectionString = connectionString;
    }

    public DatabaseUpgradeResult Migrate()
    {
        EnsureDatabase.For.SqlDatabase(_connectionString);

        UpgradeEngine upgrader = DeployChanges.To
            .SqlDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly)
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        return upgrader.PerformUpgrade();
    }
}
