using Microsoft.Data.SqlClient;
using SmsHubNext.Deployment;
using Xunit;

namespace SmsHubNext.UnitTests.Deployment;

public sealed class DatabaseConnectionStringFactoryTests
{
    [Fact]
    public void Sql_credentials_with_connection_string_metacharacters_round_trip_safely()
    {
        DatabaseSetupRequest request = new()
        {
            Server = " sql.example.local,1433 ",
            Database = " SmsHubNext ",
            Authentication = DatabaseAuthenticationMode.SqlServer,
            Username = " setup-user ",
            Password = "p;ass=word\"' فارسی",
            ConnectTimeoutSeconds = 23,
        };

        string connectionString = DatabaseConnectionStringFactory.Create(request);
        SqlConnectionStringBuilder parsed = new(connectionString);

        Assert.Equal("sql.example.local,1433", parsed.DataSource);
        Assert.Equal("SmsHubNext", parsed.InitialCatalog);
        Assert.Equal("setup-user", parsed.UserID);
        Assert.Equal(request.Password, parsed.Password);
        Assert.False(parsed.IntegratedSecurity);
        Assert.True(parsed.MultipleActiveResultSets);
        Assert.True(parsed.TrustServerCertificate);
        Assert.False(parsed.PersistSecurityInfo);
        Assert.Equal(23, parsed.ConnectTimeout);
    }

    [Fact]
    public void Windows_authentication_does_not_emit_sql_credentials()
    {
        DatabaseSetupRequest request = new()
        {
            Server = ".",
            Database = "SmsHubNext",
            Authentication = DatabaseAuthenticationMode.Windows,
        };

        SqlConnectionStringBuilder parsed = new(DatabaseConnectionStringFactory.Create(request));

        Assert.True(parsed.IntegratedSecurity);
        Assert.Empty(parsed.UserID);
        Assert.Empty(parsed.Password);
    }
}
