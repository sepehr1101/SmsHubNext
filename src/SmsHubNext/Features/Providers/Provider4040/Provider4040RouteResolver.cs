using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;

namespace SmsHubNext.Features.Providers.Provider4040;

/// <summary>Confirms an explicitly enabled ESB route on a live 4040 sender line.</summary>
public sealed class Provider4040RouteResolver
{
    private readonly Db? _db;
    private readonly IReadOnlySet<string>? _testLines;

    public Provider4040RouteResolver(Db db) => _db = db;
    public Provider4040RouteResolver(IReadOnlySet<string> testLines) => _testLines = testLines;

    public async Task<bool> UsesEsb(string line, CancellationToken cancellationToken)
    {
        if (_testLines is not null)
            return _testLines.Contains(line);
        await using SqlConnection connection = await _db!.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(Sql,
            new { LineNumber = line, ProviderCode = ProviderCodes.Provider4040 }, cancellationToken: cancellationToken));
    }

    private const string Sql =
        """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1 FROM dbo.SenderLine sl
            INNER JOIN dbo.Provider p ON p.Id = sl.ProviderId
            WHERE sl.LineNumber = @LineNumber AND sl.UseEsb = 1
              AND sl.IsActive = 1 AND sl.DeletedAtUtc IS NULL
              AND p.Code = @ProviderCode AND p.IsActive = 1 AND p.DeletedAtUtc IS NULL
        ) THEN 1 ELSE 0 END AS bit);
        """;
}
