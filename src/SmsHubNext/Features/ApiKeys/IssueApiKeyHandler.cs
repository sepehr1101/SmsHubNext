using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Security;
using System.Security.Cryptography;

namespace SmsHubNext.Features.ApiKeys;

public sealed class IssueApiKeyHandler
{
    private const string KeyLabel = "shn_";

    private readonly Db _db;

    public IssueApiKeyHandler(Db db) => _db = db;

    public async Task<Result<IssueApiKeyResponse>> Handle(
        IssueApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        string secret = GenerateSecret();
        string keyPrefix = secret[..12];
        byte[] keyHash = ApiKeyHasher.HashBytes(secret);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        try
        {
            int id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                ApiKeysSql.Insert,
                new { request.CustomerId, request.Name, KeyPrefix = keyPrefix, KeyHash = keyHash, request.ExpiresAtUtc },
                cancellationToken: cancellationToken));

            // The plaintext secret is returned once here and never persisted.
            return new IssueApiKeyResponse(id, keyPrefix, secret);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict()) // unknown customer
        {
            return Error.Validation("api_keys.unknown_customer", UserMessages.ApiKeys.UnknownCustomer);
        }
    }

    private static string GenerateSecret()
    {
        byte[] raw = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToBase64String(raw)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return KeyLabel + token;
    }
}
