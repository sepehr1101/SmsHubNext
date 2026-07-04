using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.MessageTypes;

/// <summary>Registers a message-type classification (README §4.6). The id is caller-supplied (the
/// table key is not an identity — see <see cref="CreateMessageTypeRequest.Id"/>).</summary>
public sealed class CreateMessageTypeHandler
{
    private readonly Db _db;

    public CreateMessageTypeHandler(Db db) => _db = db;

    public async Task<Result<CreateMessageTypeResponse>> Handle(
        CreateMessageTypeRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                MessageTypesSql.Insert,
                new { request.Id, request.Name, request.Code },
                cancellationToken: cancellationToken));

            return new CreateMessageTypeResponse(request.Id);
        }
        catch (SqlException ex) when (ex.IsUniqueViolation()) // duplicate Id (PK) or Code
        {
            return Error.Conflict("message_types.exists", UserMessages.ReferenceData.MessageTypeExists);
        }
    }
}
