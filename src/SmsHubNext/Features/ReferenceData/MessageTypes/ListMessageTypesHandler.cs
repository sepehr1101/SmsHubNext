using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.MessageTypes;

/// <summary>Reads the message-type classifications configured by the setup wizard or an administrator.</summary>
public sealed class ListMessageTypesHandler
{
    private readonly Db _db;

    public ListMessageTypesHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<MessageType>>> Handle(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<MessageType> rows = await connection.QueryAsync<MessageType>(
            new CommandDefinition(MessageTypesSql.List, cancellationToken: cancellationToken));

        IReadOnlyList<MessageType> messageTypes = rows.AsList();
        return Result.Success(messageTypes);
    }
}
