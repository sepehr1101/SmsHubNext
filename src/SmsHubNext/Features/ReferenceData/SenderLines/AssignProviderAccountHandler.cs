using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.SenderLines;

public sealed class AssignProviderAccountHandler
{
    private readonly Db _db;

    public AssignProviderAccountHandler(Db db) => _db = db;

    public async Task<Result> Handle(
        short senderLineId,
        AssignProviderAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (senderLineId <= 0)
            return Error.Validation("sender_lines.invalid_id", UserMessages.ReferenceData.InvalidSenderLine);

        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        SenderLineBinding? senderLine = await connection.QuerySingleOrDefaultAsync<SenderLineBinding>(
            new CommandDefinition(
                SenderLinesSql.GetBinding,
                new { Id = senderLineId },
                cancellationToken: cancellationToken));

        if (senderLine is null)
            return Error.NotFound("sender_lines.not_found", UserMessages.ReferenceData.SenderLineNotFound);

        if (request.ProviderAccountId is int providerAccountId)
        {
            Result accountValidation = await SenderLineProviderAccountRules.Validate(
                connection,
                senderLine.ProviderId,
                providerAccountId,
                cancellationToken);
            if (accountValidation.IsFailure)
                return accountValidation.Error!;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            SenderLinesSql.AssignProviderAccount,
            new { Id = senderLineId, request.ProviderAccountId },
            cancellationToken: cancellationToken));

        return Result.Success();
    }
}
