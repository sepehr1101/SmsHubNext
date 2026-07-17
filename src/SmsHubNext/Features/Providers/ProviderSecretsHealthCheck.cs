using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmsHubNext.Features.Providers.Kavenegar;
using SmsHubNext.Features.Providers.Magfa;
using SmsHubNext.Shared.Database;

namespace SmsHubNext.Features.Providers;

/// <summary>
/// Verifies the real encrypted values already stored for enabled providers. A synthetic
/// protect/unprotect round-trip could succeed with a newly-created key ring while old provider
/// secrets remain unreadable, so it would provide false confidence after a lost key ring.
/// </summary>
public sealed class ProviderSecretsHealthCheck : IHealthCheck
{
    private const string LoadActiveAccounts =
        """
        SELECT pa.Id, p.Code AS ProviderCode, pa.SecretEncrypted
        FROM dbo.ProviderAccount pa
        INNER JOIN dbo.Provider p ON p.Id = pa.ProviderId
        WHERE pa.IsActive = 1
          AND pa.DeletedAtUtc IS NULL
          AND p.IsActive = 1
          AND p.DeletedAtUtc IS NULL
          AND p.Code IN @ProviderCodes;
        """;

    private readonly Db _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IReadOnlyList<string> _enabledProviderCodes;

    public ProviderSecretsHealthCheck(
        Db db,
        ISecretProtector secretProtector,
        MagfaOptions magfaOptions,
        KavenegarOptions kavenegarOptions)
    {
        _db = db;
        _secretProtector = secretProtector;

        List<string> enabledProviderCodes = [];
        if (magfaOptions.Enabled)
            enabledProviderCodes.Add(ProviderCodes.Magfa);
        if (kavenegarOptions.Enabled)
            enabledProviderCodes.Add(ProviderCodes.Kavenegar);

        _enabledProviderCodes = enabledProviderCodes;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_enabledProviderCodes.Count == 0)
        {
            return HealthCheckResult.Healthy(
                "No external SMS provider is enabled; provider secret verification is not applicable.",
                new Dictionary<string, object>
                {
                    ["enabledProviderCount"] = 0,
                    ["providers"] = Array.Empty<ProviderSecretHealthData>(),
                });
        }

        try
        {
            await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
            IEnumerable<ProviderSecretRow> queryResult = await connection.QueryAsync<ProviderSecretRow>(
                new CommandDefinition(
                    LoadActiveAccounts,
                    new { ProviderCodes = _enabledProviderCodes.ToArray() },
                    commandTimeout: 3,
                    cancellationToken: cancellationToken));
            List<ProviderSecretRow> accounts = queryResult.ToList();
            List<ProviderSecretHealthData> providers = [];
            bool degraded = false;

            foreach (string providerCode in _enabledProviderCodes)
            {
                List<ProviderSecretRow> providerAccounts = accounts
                    .Where(account => string.Equals(account.ProviderCode, providerCode, StringComparison.Ordinal))
                    .ToList();
                int decryptableAccountCount = 0;

                foreach (ProviderSecretRow account in providerAccounts)
                {
                    try
                    {
                        _secretProtector.Unprotect(account.SecretEncrypted);
                        decryptableAccountCount++;
                    }
                    catch (Exception)
                    {
                        // The result intentionally exposes only counts, never secret material or crypto details.
                    }
                }

                if (providerAccounts.Count == 0 || decryptableAccountCount != providerAccounts.Count)
                    degraded = true;

                providers.Add(new ProviderSecretHealthData(
                    providerCode,
                    providerAccounts.Count,
                    decryptableAccountCount,
                    providerAccounts.Count - decryptableAccountCount));
            }

            Dictionary<string, object> data = new()
            {
                ["enabledProviderCount"] = _enabledProviderCodes.Count,
                ["providers"] = providers,
            };

            return degraded
                ? HealthCheckResult.Degraded("One or more enabled providers have no usable active credential.", data: data)
                : HealthCheckResult.Healthy("Enabled provider credentials can be decrypted.", data);
        }
        catch (Exception)
        {
            // SQL connectivity has its own Unhealthy readiness check. Keep this capability-specific
            // check Degraded so a duplicated dependency failure doesn't obscure the primary cause.
            return HealthCheckResult.Degraded("Provider credentials could not be verified.");
        }
    }

    private sealed record ProviderSecretRow(int Id, string ProviderCode, byte[] SecretEncrypted);
}

public sealed record ProviderSecretHealthData(
    string ProviderCode,
    int ActiveAccountCount,
    int DecryptableAccountCount,
    int InvalidAccountCount);
