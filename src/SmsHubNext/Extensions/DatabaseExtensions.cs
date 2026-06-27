using SmsHubNext.Shared.Database;

namespace SmsHubNext.Extensions;

/// <summary>
/// Database bootstrap for the composition root. Applies forward-only migrations at
/// startup (idempotent; fail fast). See ARCHITECTURE.md §10.
/// </summary>
public static class DatabaseExtensions
{
    public static void MigrateDatabase(this WebApplication app)
    {
        var connectionString = app.Configuration.GetConnectionString(Db.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{Db.ConnectionStringName}' is not configured.");

        var migration = new DatabaseMigrator(connectionString).Migrate();
        if (!migration.Successful)
            throw new InvalidOperationException("Database migration failed.", migration.Error);
    }
}
