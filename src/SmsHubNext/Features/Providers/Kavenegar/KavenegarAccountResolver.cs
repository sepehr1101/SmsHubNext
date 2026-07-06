using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Shared.Database;

namespace SmsHubNext.Features.Providers.Kavenegar;

public sealed class KavenegarAccountResolver
{
    private readonly Db _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IReadOnlyList<KavenegarAccount>? _configuredAccounts;

    public KavenegarAccountResolver(Db db, ISecretProtector secretProtector)
    {
        _db = db;
        _secretProtector = secretProtector;
    }

    public KavenegarAccountResolver(IReadOnlyList<KavenegarAccount> configuredAccounts)
    {
        _db = null!;
        _secretProtector = null!;
        _configuredAccounts = configuredAccounts;
    }

    public async Task<IReadOnlyList<KavenegarAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        if (_configuredAccounts is not null)
            return _configuredAccounts;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<KavenegarAccountRow> rows = await connection.QueryAsync<KavenegarAccountRow>(
            new CommandDefinition(Sql, new
            {
                ProviderCode = ProviderCodes.Kavenegar,
                AuthType = ProviderAccountAuthTypes.ApiKey,
            }, cancellationToken: cancellationToken));

        List<KavenegarAccount> accounts = new List<KavenegarAccount>();
        foreach (IGrouping<int, KavenegarAccountRow> group in rows.GroupBy(row => row.Id))
        {
            KavenegarAccountRow first = group.First();
            List<string> senderLines = group
                .Select(row => row.LineNumber)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line!)
                .ToList();

            accounts.Add(new KavenegarAccount
            {
                ApiKey = _secretProtector.Unprotect(first.SecretEncrypted),
                SenderLines = senderLines,
                InboundLines = senderLines,
            });
        }

        return accounts;
    }

    public async Task<KavenegarAccount?> ResolveAsync(string senderLine, CancellationToken cancellationToken)
    {
        IReadOnlyList<KavenegarAccount> accounts = await GetAccountsAsync(cancellationToken);
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
        LEFT JOIN dbo.SenderLine sl ON sl.ProviderAccountId = pa.Id AND sl.IsActive = 1
        WHERE p.Code = @ProviderCode
          AND pa.AuthType = @AuthType
          AND pa.IsActive = 1
        ORDER BY pa.Id, sl.LineNumber;
        """;

    private sealed record KavenegarAccountRow(
        int Id,
        byte[] SecretEncrypted,
        string? LineNumber);
}
