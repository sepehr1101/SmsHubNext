using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ProviderAccounts;

public sealed class DeleteProviderAccountHandler
{
    private readonly Db _db;

    public DeleteProviderAccountHandler(Db db) => _db = db;

    public async Task<Result> Handle(int id, int deletedByApiKeyId, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("provider_accounts.invalid_id", UserMessages.ProviderAccounts.InvalidId);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            ProviderAccountsSql.SoftDelete,
            new { Id = id, DeletedByApiKeyId = deletedByApiKeyId },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("provider_accounts.not_found", UserMessages.ProviderAccounts.NotFound)
            : Result.Success();
    }
}
