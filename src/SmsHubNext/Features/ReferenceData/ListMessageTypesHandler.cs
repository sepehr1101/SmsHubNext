using Dapper;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

/// <summary>Reads the seeded message-type classifications.</summary>
public sealed class ListMessageTypesHandler
{
    private readonly Db _db;

    public ListMessageTypesHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<MessageType>>> Handle(CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<MessageType>(
            new CommandDefinition(MessageTypesSql.List, cancellationToken: cancellationToken));

        IReadOnlyList<MessageType> messageTypes = rows.AsList();
        return Result.Success(messageTypes);
    }
}
