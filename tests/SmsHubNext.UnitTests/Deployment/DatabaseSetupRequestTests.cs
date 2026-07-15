using SmsHubNext.Deployment;
using Xunit;

namespace SmsHubNext.UnitTests.Deployment;

public sealed class DatabaseSetupRequestTests
{
    [Fact]
    public void Windows_authentication_does_not_require_credentials()
    {
        DatabaseSetupRequest request = new()
        {
            Server = "sql.example.local",
            Database = "SmsHubNext",
            Authentication = DatabaseAuthenticationMode.Windows,
        };

        Assert.Empty(request.Validate());
    }

    [Fact]
    public void Sql_authentication_requires_username_and_password()
    {
        DatabaseSetupRequest request = new()
        {
            Server = "sql.example.local",
            Database = "SmsHubNext",
            Authentication = DatabaseAuthenticationMode.SqlServer,
        };

        IReadOnlyList<string> errors = request.Validate();

        Assert.Contains("Username is required.", errors);
        Assert.Contains("Password is required.", errors);
    }

    [Theory]
    [InlineData("server\nname", "SmsHubNext")]
    [InlineData("server", "SmsHubNext\rtest")]
    public void Line_breaks_are_rejected(string server, string database)
    {
        DatabaseSetupRequest request = new()
        {
            Server = server.Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\r", "\r", StringComparison.Ordinal),
            Database = database.Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\r", "\r", StringComparison.Ordinal),
            Authentication = DatabaseAuthenticationMode.Windows,
        };

        Assert.Contains(request.Validate(), error => error.Contains("line breaks", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Connection_timeout_is_bounded(int timeout)
    {
        DatabaseSetupRequest request = new()
        {
            Server = "server",
            Database = "SmsHubNext",
            Authentication = DatabaseAuthenticationMode.Windows,
            ConnectTimeoutSeconds = timeout,
        };

        Assert.Contains(request.Validate(), error => error.Contains("ConnectTimeoutSeconds", StringComparison.Ordinal));
    }
}
