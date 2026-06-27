using System.Data;
using SmsHubNext.Shared.Database;
using Xunit;

namespace SmsHubNext.UnitTests.Shared.Database;

public class DbTests
{
    private const string AnyConnectionString =
        "Server=localhost;Database=SmsHubNext;Trusted_Connection=True;TrustServerCertificate=True";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_a_connection_string(string connectionString)
    {
        Assert.Throws<ArgumentException>(() => new Db(connectionString));
    }

    [Fact]
    public void CreateConnection_returns_a_new_unopened_connection()
    {
        var db = new Db(AnyConnectionString);

        using var connection = db.CreateConnection();

        Assert.Equal(ConnectionState.Closed, connection.State);
    }
}
