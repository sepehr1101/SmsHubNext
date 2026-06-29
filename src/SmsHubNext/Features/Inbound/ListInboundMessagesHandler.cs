using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Inbound;

/// <summary>Lists received (MO) messages newest-first, optionally filtered to one receiving number.</summary>
public sealed class ListInboundMessagesHandler
{
    private const int MaxTake = 500;
    private const int DefaultTake = 100;

    private readonly Db _db;

    public ListInboundMessagesHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<InboundMessage>>> Handle(
        string? recipientNumber, int take, CancellationToken cancellationToken)
    {
        int limit = take is < 1 or > MaxTake ? DefaultTake : take;
        string? filter = string.IsNullOrWhiteSpace(recipientNumber) ? null : recipientNumber;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<InboundMessage> rows = await connection.QueryAsync<InboundMessage>(new CommandDefinition(
            InboundSql.ListRecent,
            new { RecipientNumber = filter, Take = limit },
            cancellationToken: cancellationToken));

        IReadOnlyList<InboundMessage> messages = rows.AsList();
        return Result.Success(messages);
    }
}
