using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Security;

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
        var validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        var secret = GenerateSecret();
        var keyPrefix = secret[..12];
        var keyHash = ApiKeyHasher.HashBytes(secret);

        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        try
        {
            var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                ApiKeysSql.Insert,
                new { request.CustomerId, request.Name, KeyPrefix = keyPrefix, KeyHash = keyHash, request.ExpiresAtUtc },
                cancellationToken: cancellationToken));

            // The plaintext secret is returned once here and never persisted.
            return new IssueApiKeyResponse(id, keyPrefix, secret);
        }
        catch (SqlException ex) when (ex.Number == 547) // FK violation: unknown customer
        {
            return Error.Validation("api_keys.unknown_customer", "The customer does not exist.");
        }
    }

    private static string GenerateSecret()
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(raw)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return KeyLabel + token;
    }
}
