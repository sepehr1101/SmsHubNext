using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

public sealed class UpdateApiKeyHandler
{
    private readonly Db _db;

    public UpdateApiKeyHandler(Db db) => _db = db;

    public async Task<Result> Handle(int id, UpdateApiKeyRequest request, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("api_keys.invalid_id", UserMessages.ApiKeys.InvalidId);

        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        ApiKeyState? key = await connection.QuerySingleOrDefaultAsync<ApiKeyState>(new CommandDefinition(
            ApiKeysSql.GetState,
            new { Id = id },
            cancellationToken: cancellationToken));

        if (key is null)
            return Error.NotFound("api_keys.not_found", UserMessages.ApiKeys.NotFound);
        if (key.RevokedAtUtc is not null)
            return Error.Conflict("api_keys.revoked", UserMessages.ApiKeys.RevokedCannotChange);

        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            ApiKeysSql.Update,
            new { Id = id, request.Name, request.ExpiresAtUtc, request.IsActive },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.Conflict("api_keys.revoked", UserMessages.ApiKeys.RevokedCannotChange)
            : Result.Success();
    }
}

internal sealed record ApiKeyState(DateTime? RevokedAtUtc);
