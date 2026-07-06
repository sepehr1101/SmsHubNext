using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Shared.Database;

namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// Maps sender lines to the active <see cref="MagfaAccount"/> rows stored in dbo.ProviderAccount.
/// <see cref="MagfaSmsProvider"/> uses it to pick per-request credentials on send and to fan out
/// account-scoped reads (statuses/mid/inbox) that have no per-message line to key on.
/// </summary>
public sealed class MagfaAccountResolver
{
    private readonly Db _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IReadOnlyList<MagfaAccount>? _configuredAccounts;

    public MagfaAccountResolver(Db db, ISecretProtector secretProtector)
    {
        _db = db;
        _secretProtector = secretProtector;
    }

    public MagfaAccountResolver(IReadOnlyList<MagfaAccount> configuredAccounts)
    {
        _db = null!;
        _secretProtector = null!;
        _configuredAccounts = configuredAccounts;
    }

    /// <summary>Every active database account, for the reads that fan out over all of them.</summary>
    public async Task<IReadOnlyList<MagfaAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        if (_configuredAccounts is not null)
            return _configuredAccounts;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<MagfaAccountRow> rows = await connection.QueryAsync<MagfaAccountRow>(
            new CommandDefinition(Sql, new
            {
                ProviderCode = ProviderCodes.Magfa,
                AuthType = ProviderAccountAuthTypes.UsernamePasswordDomain,
            }, cancellationToken: cancellationToken));

        List<MagfaAccount> accounts = new List<MagfaAccount>();
        foreach (IGrouping<int, MagfaAccountRow> group in rows.GroupBy(row => row.Id))
        {
            MagfaAccountRow first = group.First();
            IReadOnlyDictionary<string, string> settings = ProviderAccountSettings.FromJson(first.SettingsJson);
            string username = settings.TryGetValue("username", out string? usernameValue) ? usernameValue : string.Empty;
            string domain = settings.TryGetValue("domain", out string? domainValue) ? domainValue : string.Empty;
            string password = _secretProtector.Unprotect(first.SecretEncrypted);

            accounts.Add(new MagfaAccount
            {
                Username = username,
                Domain = domain,
                Password = password,
                SenderLines = group
                    .Select(row => row.LineNumber)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line!)
                    .ToList(),
            });
        }

        return accounts;
    }

    /// <summary>The account that owns <paramref name="senderLine"/>, or <c>null</c> if none does.</summary>
    public async Task<MagfaAccount?> ResolveAsync(string senderLine, CancellationToken cancellationToken)
    {
        IReadOnlyList<MagfaAccount> accounts = await GetAccountsAsync(cancellationToken);
        return accounts.FirstOrDefault(account =>
            account.SenderLines.Any(line => string.Equals(line, senderLine, StringComparison.OrdinalIgnoreCase)));
    }

    private const string Sql =
        """
        SELECT
            pa.Id,
            pa.SettingsJson,
            pa.SecretEncrypted,
            sl.LineNumber
        FROM dbo.ProviderAccount pa
        INNER JOIN dbo.Provider p ON p.Id = pa.ProviderId
        LEFT JOIN dbo.SenderLine sl ON sl.ProviderAccountId = pa.Id AND sl.IsActive = 1
        WHERE p.Code = @ProviderCode
          AND pa.AuthType = @AuthType
          AND pa.IsActive = 1
        ORDER BY pa.Id, sl.LineNumber;
        """;

    private sealed record MagfaAccountRow(
        int Id,
        string SettingsJson,
        byte[] SecretEncrypted,
        string? LineNumber);
}
