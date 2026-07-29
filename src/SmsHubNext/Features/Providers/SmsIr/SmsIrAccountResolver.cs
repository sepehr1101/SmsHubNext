using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Shared.Database;

namespace SmsHubNext.Features.Providers.SmsIr;

public sealed class SmsIrAccountResolver
{
    private readonly Db _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IReadOnlyList<SmsIrAccount>? _configuredAccounts;

    public SmsIrAccountResolver(Db db, ISecretProtector secretProtector)
    {
        _db = db;
        _secretProtector = secretProtector;
    }

    public SmsIrAccountResolver(IReadOnlyList<SmsIrAccount> configuredAccounts)
    {
        _db = null!;
        _secretProtector = null!;
        _configuredAccounts = configuredAccounts;
    }

    public async Task<IReadOnlyList<SmsIrAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        if (_configuredAccounts is not null)
            return _configuredAccounts;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<SmsIrAccountRow> rows = await connection.QueryAsync<SmsIrAccountRow>(
            new CommandDefinition(Sql, new
            {
                ProviderCode = ProviderCodes.SmsIr,
                AuthType = ProviderAccountAuthTypes.ApiKey,
            }, cancellationToken: cancellationToken));

        List<SmsIrAccount> accounts = new List<SmsIrAccount>();
        foreach (IGrouping<int, SmsIrAccountRow> group in rows.GroupBy(row => row.Id))
        {
            SmsIrAccountRow first = group.First();
            List<string> senderLines = group
                .Select(row => row.LineNumber)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line!)
                .ToList();

            accounts.Add(new SmsIrAccount
            {
                ApiKey = _secretProtector.Unprotect(first.SecretEncrypted),
                SenderLines = senderLines,
            });
        }

        return accounts;
    }

    public async Task<SmsIrAccount?> ResolveAsync(string senderLine, CancellationToken cancellationToken)
    {
        IReadOnlyList<SmsIrAccount> accounts = await GetAccountsAsync(cancellationToken);
        return accounts.FirstOrDefault(account =>
            account.SenderLines.Any(line => string.Equals(line, senderLine, StringComparison.OrdinalIgnoreCase)));
    }

    private const string Sql =
        """
        SELECT
            pa.Id,
            pa.SecretEncrypted,
            sl.LineNumber
        FROM dbo.ProviderAccount pa
        INNER JOIN dbo.Provider p ON p.Id = pa.ProviderId
        LEFT JOIN dbo.SenderLine sl
            ON sl.ProviderAccountId = pa.Id
           AND sl.IsActive = 1
           AND sl.DeletedAtUtc IS NULL
        WHERE p.Code = @ProviderCode
          AND p.IsActive = 1
          AND p.DeletedAtUtc IS NULL
          AND pa.AuthType = @AuthType
          AND pa.IsActive = 1
          AND pa.DeletedAtUtc IS NULL
        ORDER BY pa.Id, sl.LineNumber;
        """;

    private sealed record SmsIrAccountRow(
        int Id,
        byte[] SecretEncrypted,
        string? LineNumber);
}
