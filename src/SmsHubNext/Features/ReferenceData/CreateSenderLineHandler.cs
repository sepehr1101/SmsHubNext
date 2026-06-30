using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

/// <summary>Registers a sending line for a provider (README §4.5).</summary>
public sealed class CreateSenderLineHandler
{
    private readonly Db _db;

    public CreateSenderLineHandler(Db db) => _db = db;

    public async Task<Result<CreateSenderLineResponse>> Handle(
        CreateSenderLineRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        try
        {
            short id = await connection.ExecuteScalarAsync<short>(new CommandDefinition(
                SenderLinesSql.Insert,
                new { request.ProviderId, request.LineNumber, request.IsSharedLine, request.IsActive },
                cancellationToken: cancellationToken));

            return new CreateSenderLineResponse(id);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict()) // unknown provider
        {
            return Error.Validation("sender_lines.unknown_provider", "The provider does not exist.");
        }
    }
}
