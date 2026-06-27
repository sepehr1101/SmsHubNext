using Dapper;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

/// <summary>Reads the configured sending lines.</summary>
public sealed class ListSenderLinesHandler
{
    private readonly Db _db;

    public ListSenderLinesHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<SenderLine>>> Handle(CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<SenderLine>(
            new CommandDefinition(SenderLinesSql.List, cancellationToken: cancellationToken));

        IReadOnlyList<SenderLine> senderLines = rows.AsList();
        return Result.Success(senderLines);
    }
}
