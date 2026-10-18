using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Security;

namespace SmsHubNext.Features.ApiKeys;

public sealed class RenewApiKeyHandler(Db db)
{
    public async Task<Result<IssueApiKeyResponse>> Handle(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("api_keys.invalid_id", UserMessages.ApiKeys.InvalidId);

        await using SqlConnection connection = await db.OpenConnectionAsync(cancellationToken);
        ApiKeyState? state = await connection.QuerySingleOrDefaultAsync<ApiKeyState>(new CommandDefinition(
            ApiKeysSql.GetState, new { Id = id }, cancellationToken: cancellationToken));
        if (state is null)
            return Error.NotFound("api_keys.not_found", UserMessages.ApiKeys.NotFound);
        if (state.RevokedAtUtc is not null)
            return Error.Conflict("api_keys.revoked", UserMessages.ApiKeys.RevokedCannotChange);

        GeneratedApiKey key = ApiKeySecretGenerator.Generate();
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            ApiKeysSql.Renew,
            new { Id = id, KeyPrefix = key.Prefix, KeyHash = key.Hash },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.Conflict("api_keys.revoked", UserMessages.ApiKeys.RevokedCannotChange)
            : new IssueApiKeyResponse(id, key.Prefix, key.Secret);
    }
}
