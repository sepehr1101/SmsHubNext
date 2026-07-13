using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.SenderLines;

public sealed class DeleteSenderLineHandler
{
    private readonly Db _db;

    public DeleteSenderLineHandler(Db db) => _db = db;

    public async Task<Result> Handle(short id, int deletedByApiKeyId, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("sender_lines.invalid_id", UserMessages.ReferenceData.InvalidSenderLine);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            SenderLinesSql.SoftDelete,
            new { Id = id, DeletedByApiKeyId = deletedByApiKeyId },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("sender_lines.not_found", UserMessages.ReferenceData.SenderLineNotFound)
            : Result.Success();
    }
}
