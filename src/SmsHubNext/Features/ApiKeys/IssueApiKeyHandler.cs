using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Security;

namespace SmsHubNext.Features.ApiKeys;

public sealed class IssueApiKeyHandler
{
    private readonly Db _db;

    public IssueApiKeyHandler(Db db) => _db = db;

    public async Task<Result<IssueApiKeyResponse>> Handle(
        IssueApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        GeneratedApiKey key = ApiKeySecretGenerator.Generate();

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        try
        {
            int? id = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                ApiKeysSql.Insert,
                new
                {
                    request.CustomerId,
                    request.Name,
                    KeyPrefix = key.Prefix,
                    KeyHash = key.Hash,
                    request.ExpiresAtUtc,
                },
                cancellationToken: cancellationToken));

            // The plaintext secret is returned once here and never persisted.
            if (id is null)
                return Error.Validation("api_keys.unknown_customer", UserMessages.ApiKeys.UnknownCustomer);

            return new IssueApiKeyResponse(id.Value, key.Prefix, key.Secret);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict()) // unknown customer
        {
            return Error.Validation("api_keys.unknown_customer", UserMessages.ApiKeys.UnknownCustomer);
        }
    }

}
