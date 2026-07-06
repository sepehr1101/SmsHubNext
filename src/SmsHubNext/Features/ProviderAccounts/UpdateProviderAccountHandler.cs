using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ProviderAccounts;

public sealed class UpdateProviderAccountHandler
{
    private readonly Db _db;
    private readonly ISecretProtector _secretProtector;

    public UpdateProviderAccountHandler(Db db, ISecretProtector secretProtector)
    {
        _db = db;
        _secretProtector = secretProtector;
    }

    public async Task<Result> Handle(
        int id,
        UpdateProviderAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("provider_accounts.invalid_id", UserMessages.ProviderAccounts.InvalidId);

        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        string settingsJson = ProviderAccountSettings.ToJson(request.Settings);
        byte[]? secretEncrypted = request.Secret is null ? null : _secretProtector.Protect(request.Secret);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        int rows = await connection.ExecuteAsync(new CommandDefinition(
            secretEncrypted is null ? ProviderAccountsSql.UpdateWithoutSecret : ProviderAccountsSql.UpdateWithSecret,
            new
            {
                Id = id,
                request.ProviderCode,
                request.DisplayName,
                request.AuthType,
                SettingsJson = settingsJson,
                SecretEncrypted = secretEncrypted,
                request.IsActive,
            },
            cancellationToken: cancellationToken));

        if (rows == 0)
            return Error.NotFound("provider_accounts.not_found", UserMessages.ProviderAccounts.NotFound);

        return Result.Success();
    }
}
