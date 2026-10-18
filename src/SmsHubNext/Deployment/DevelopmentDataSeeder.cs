using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Security;

namespace SmsHubNext.Deployment;

public sealed class DevelopmentDataSeeder
{
    public const string AdministratorApiKey = "shn_dev_admin_1234567890";
    public const string CustomerApiKey = "shn_dev_customer_123456";

    private readonly Db _db;
    private readonly ISecretProtector _secretProtector;

    public DevelopmentDataSeeder(Db db, ISecretProtector secretProtector)
    {
        _db = db;
        _secretProtector = secretProtector;
    }

    public async Task Seed(CancellationToken cancellationToken)
    {
        byte[] administratorKeyHash = ApiKeyHasher.HashBytes(AdministratorApiKey);
        byte[] customerKeyHash = ApiKeyHasher.HashBytes(CustomerApiKey);
        byte[] providerSecret = _secretProtector.Protect("development-password");

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        using SqlTransaction transaction = connection.BeginTransaction();
        await connection.ExecuteAsync(new CommandDefinition(
            DevelopmentDataSql.Seed,
            new
            {
                AdministratorKeyHash = administratorKeyHash,
                CustomerKeyHash = customerKeyHash,
                ProviderSecret = providerSecret,
            },
            transaction,
            cancellationToken: cancellationToken));
        transaction.Commit();
    }
}
