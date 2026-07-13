using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.SenderLines;

public sealed class UpdateSenderLineHandler
{
    private readonly Db _db;

    public UpdateSenderLineHandler(Db db) => _db = db;

    public async Task<Result> Handle(
        short id,
        UpdateSenderLineRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("sender_lines.invalid_id", UserMessages.ReferenceData.InvalidSenderLine);

        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        SenderLineBinding? senderLine = await connection.QuerySingleOrDefaultAsync<SenderLineBinding>(
            new CommandDefinition(SenderLinesSql.GetBinding, new { Id = id }, cancellationToken: cancellationToken));

        if (senderLine is null)
            return Error.NotFound("sender_lines.not_found", UserMessages.ReferenceData.SenderLineNotFound);

        Result references = await ValidateReferences(connection, senderLine.ProviderId, request, cancellationToken);
        if (references.IsFailure)
            return references.Error!;

        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            SenderLinesSql.Update,
            new
            {
                Id = id,
                request.LineNumber,
                request.IsSharedLine,
                request.CustomerId,
                request.ProviderAccountId,
                request.IsActive,
            },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("sender_lines.not_found", UserMessages.ReferenceData.SenderLineNotFound)
            : Result.Success();
    }

    private static async Task<Result> ValidateReferences(
        SqlConnection connection,
        byte providerId,
        UpdateSenderLineRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CustomerId is short customerId)
        {
            bool customerExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                SenderLinesSql.CustomerExists,
                new { CustomerId = customerId },
                cancellationToken: cancellationToken));

            if (!customerExists)
                return Error.Validation("sender_lines.unknown_customer", UserMessages.ReferenceData.SenderLineUnknownCustomer);
        }

        if (request.ProviderAccountId is int providerAccountId)
        {
            return await SenderLineProviderAccountRules.Validate(
                connection,
                providerId,
                providerAccountId,
                cancellationToken);
        }

        return Result.Success();
    }
}
