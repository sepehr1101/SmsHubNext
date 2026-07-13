using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.MessageTypes;

public sealed class DeleteMessageTypeHandler
{
    private readonly Db _db;

    public DeleteMessageTypeHandler(Db db) => _db = db;

    public async Task<Result> Handle(byte id, int deletedByApiKeyId, CancellationToken cancellationToken)
    {
        if (id == 0)
            return Error.Validation("message_types.invalid_id", UserMessages.ReferenceData.InvalidMessageType);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            MessageTypesSql.SoftDelete,
            new { Id = id, DeletedByApiKeyId = deletedByApiKeyId },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("message_types.not_found", UserMessages.ReferenceData.MessageTypeNotFound)
            : Result.Success();
    }
}
