using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Security;

namespace SmsHubNext.Deployment;

public sealed class AdminApiKeyBootstrapper
{
    private readonly Db _db;

    public AdminApiKeyBootstrapper(Db db) => _db = db;

    public async Task<SetupAdministratorCredential> CreateAsync(
        string name,
        DateTime? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("نام کلید مدیریتی الزامی است.", nameof(name));
        if (name.Trim().Length > 100)
            throw new ArgumentException("نام کلید مدیریتی حداکثر می‌تواند ۱۰۰ نویسه باشد.", nameof(name));

        GeneratedApiKey key = ApiKeySecretGenerator.Generate();
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        using SqlTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        try
        {
            int id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                AdminApiKeyBootstrapSql.Insert,
                new
                {
                    Name = name.Trim(),
                    KeyPrefix = key.Prefix,
                    KeyHash = key.Hash,
                    AccessLevel = (byte)ApiKeyAccessLevel.Administrator,
                    ExpiresAtUtc = expiresAtUtc,
                },
                transaction,
                cancellationToken: cancellationToken));

            transaction.Commit();
            return new SetupAdministratorCredential(id, key.Prefix, key.Secret);
        }
        catch
        {
            if (transaction.Connection is not null)
                transaction.Rollback();

            throw;
        }
    }
}
