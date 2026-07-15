using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Deployment;
using SmsHubNext.IntegrationTests.Shared;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Deployment;

public sealed class SetupCommandIntegrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SmsHubNext.SetupIntegration.{Guid.NewGuid():N}");
    private DatabaseSetupRequest _request = null!;
    private string _databaseName = null!;
    private string _masterConnectionString = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        Directory.CreateDirectory(_directory);

        SqlConnectionStringBuilder container = new(_sqlServer.GetConnectionString());
        _masterConnectionString = container.ConnectionString;
        _databaseName = $"SmsHubNextSetup{Guid.NewGuid():N}";

        _request = new DatabaseSetupRequest
        {
            Server = container.DataSource,
            Database = _databaseName,
            Authentication = DatabaseAuthenticationMode.SqlServer,
            Username = container.UserID,
            Password = container.Password,
            ConnectTimeoutSeconds = 15,
            TrustServerCertificate = true,
        };
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        await _sqlServer.DisposeAsync();
    }

    [Fact]
    public async Task Configure_command_tests_connection_and_writes_settings_without_creating_database()
    {
        string requestPath = Path.Combine(_directory, "database-request.json");
        string resultPath = Path.Combine(_directory, "configure-result.json");
        string settingsPath = Path.Combine(_directory, "appsettings.Production.json");
        string keyRingPath = Path.Combine(_directory, "keys");
        await File.WriteAllTextAsync(
            requestPath,
            System.Text.Json.JsonSerializer.Serialize(
                _request,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                }));

        int? configureExitCode = await SetupCommandRunner.TryRunAsync(
            [
                "setup", "configure",
                "--request", requestPath,
                "--settings", settingsPath,
                "--key-ring", keyRingPath,
                "--result", resultPath,
            ]);

        Assert.Equal(SetupExitCodes.Success, configureExitCode);
        Assert.True(File.Exists(settingsPath));
        string settings = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains(_databaseName, settings, StringComparison.Ordinal);

        await using SqlConnection connection = new(_masterConnectionString);
        int? databaseId = await connection.ExecuteScalarAsync<int?>(
            "SELECT DB_ID(@databaseName);",
            new { databaseName = _databaseName });
        Assert.Null(databaseId);
    }
}
