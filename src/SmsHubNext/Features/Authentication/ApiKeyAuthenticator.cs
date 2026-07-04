using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Security;
using System.Net;

namespace SmsHubNext.Features.Authentication;

/// <summary>
/// Resolves a raw API key to the caller it represents (README auth model, §4.2/§4.3):
/// hashes the key, seeks the active/non-revoked/non-expired <c>ApiKey</c> row, and
/// enforces the optional per-key CIDR allow-list against the caller's IP.
///
/// This is the whole of API-key auth. It is wired as a service and exercised by the
/// convenience <c>GET /auth/whoami</c> endpoint, but it is deliberately NOT placed in
/// the request pipeline yet — every other endpoint stays open for convenient testing.
/// Activation is a one-liner: see <see cref="ApiKeyAuthenticationMiddleware"/> and
/// <c>ApplicationBuilderExtensions</c>.
/// </summary>
public sealed class ApiKeyAuthenticator
{
    private readonly Db _db;
    private readonly TimeProvider _clock;

    public ApiKeyAuthenticator(Db db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<ApiKeyIdentity>> Authenticate(
        string? rawKey,
        IPAddress? remoteIp,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return Error.Unauthorized("auth.missing_key", UserMessages.Auth.MissingKey);

        byte[] keyHash = ApiKeyHasher.HashBytes(rawKey.Trim());

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        KeyRow? key = await connection.QuerySingleOrDefaultAsync<KeyRow>(new CommandDefinition(
            AuthenticationSql.ResolveByHash,
            new { KeyHash = keyHash },
            cancellationToken: cancellationToken));

        if (key is null)
            return Error.Unauthorized("auth.invalid_key", UserMessages.Auth.InvalidKey);

        if (!key.IsActive || key.RevokedAtUtc is not null)
            return Error.Unauthorized("auth.inactive_key", UserMessages.Auth.InactiveKey);

        if (key.ExpiresAtUtc is not null && key.ExpiresAtUtc.Value <= _clock.GetUtcNow().UtcDateTime)
            return Error.Unauthorized("auth.expired_key", UserMessages.Auth.ExpiredKey);

        List<string> cidrs = (await connection.QueryAsync<string>(new CommandDefinition(
            AuthenticationSql.ListRestrictions,
            new { key.ApiKeyId },
            cancellationToken: cancellationToken))).AsList();

        // An empty allow-list means "no IP restriction"; otherwise the caller IP must match one entry.
        if (cidrs.Count > 0 && !IsAllowed(remoteIp, cidrs))
            return Error.Unauthorized("auth.ip_not_allowed", UserMessages.Auth.IpNotAllowed);

        return new ApiKeyIdentity(key.ApiKeyId, key.CustomerId, key.KeyPrefix);
    }

    private static bool IsAllowed(IPAddress? remoteIp, IReadOnlyList<string> cidrs)
    {
        if (remoteIp is null)
            return false;

        foreach (string cidr in cidrs)
        {
            if (IPNetwork.TryParse(cidr, out IPNetwork network) && network.Contains(remoteIp))
                return true;
        }

        return false;
    }

    private sealed record KeyRow(
        int ApiKeyId,
        short CustomerId,
        string KeyPrefix,
        bool IsActive,
        DateTime? ExpiresAtUtc,
        DateTime? RevokedAtUtc);
}
