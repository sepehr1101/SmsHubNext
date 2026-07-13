using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.MessageTypes;

public sealed class UpdateMessageTypeHandler
{
    private readonly Db _db;

    public UpdateMessageTypeHandler(Db db) => _db = db;

    public async Task<Result> Handle(byte id, UpdateMessageTypeRequest request, CancellationToken cancellationToken)
    {
        if (id == 0)
            return Error.Validation("message_types.invalid_id", UserMessages.ReferenceData.InvalidMessageType);

        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            MessageTypesSql.Update,
            new { Id = id, request.Name, request.IsActive },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("message_types.not_found", UserMessages.ReferenceData.MessageTypeNotFound)
            : Result.Success();
    }
}
