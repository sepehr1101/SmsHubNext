using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ProviderAccounts;

public sealed class CreateProviderAccountHandler
{
    private readonly Db _db;
    private readonly ISecretProtector _secretProtector;

    public CreateProviderAccountHandler(Db db, ISecretProtector secretProtector)
    {
        _db = db;
        _secretProtector = secretProtector;
    }

    public async Task<Result<CreateProviderAccountResponse>> Handle(
        CreateProviderAccountRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        byte[] secretEncrypted = _secretProtector.Protect(request.Secret);
        string settingsJson = ProviderAccountSettings.ToJson(request.Settings);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        int id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ProviderAccountsSql.Insert,
            new
            {
                request.ProviderCode,
                request.DisplayName,
                request.AuthType,
                SettingsJson = settingsJson,
                SecretEncrypted = secretEncrypted,
                request.IsActive,
            },
            cancellationToken: cancellationToken));

        if (id == 0)
            return Error.Validation("provider_accounts.unknown_provider", UserMessages.ProviderAccounts.UnknownProvider);

        return new CreateProviderAccountResponse(id);
    }
}
