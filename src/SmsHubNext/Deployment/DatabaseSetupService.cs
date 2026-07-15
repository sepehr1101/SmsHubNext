using Microsoft.Data.SqlClient;

namespace SmsHubNext.Deployment;

public sealed class DatabaseSetupService
{
    public async Task TestConnectionAsync(
        DatabaseSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        string targetConnectionString = DatabaseConnectionStringFactory.Create(request);
        SqlConnectionStringBuilder masterBuilder = new(targetConnectionString)
        {
            InitialCatalog = "master",
        };

        await using SqlConnection masterConnection = new(masterBuilder.ConnectionString);
        await masterConnection.OpenAsync(cancellationToken);

        await using SqlCommand databaseLookup = masterConnection.CreateCommand();
        databaseLookup.CommandText = "SELECT DB_ID(@databaseName);";
        databaseLookup.CommandTimeout = request.ConnectTimeoutSeconds;
        databaseLookup.Parameters.AddWithValue("@databaseName", request.Database.Trim());
        object? databaseId = await databaseLookup.ExecuteScalarAsync(cancellationToken);

        // A missing database is valid during first installation: DatabaseMigrator creates it.
        // An existing database is opened as well so inaccessible/offline databases fail early.
        if (databaseId is null or DBNull)
            return;

        await using SqlConnection targetConnection = new(targetConnectionString);
        await targetConnection.OpenAsync(cancellationToken);

        await using SqlCommand command = targetConnection.CreateCommand();
        command.CommandText = "SELECT 1;";
        command.CommandTimeout = request.ConnectTimeoutSeconds;
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
