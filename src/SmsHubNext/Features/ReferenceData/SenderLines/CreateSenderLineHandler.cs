using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.SenderLines;

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

        bool providerExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SenderLinesSql.ProviderExists,
            new { request.ProviderId },
            cancellationToken: cancellationToken));
        if (!providerExists)
            return Error.Validation("sender_lines.unknown_provider", UserMessages.ReferenceData.SenderLineUnknownProvider);

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
            Result accountValidation = await SenderLineProviderAccountRules.Validate(
                connection,
                request.ProviderId,
                providerAccountId,
                cancellationToken);
            if (accountValidation.IsFailure)
                return accountValidation.Error!;
        }

        try
        {
            short? id = await connection.ExecuteScalarAsync<short?>(new CommandDefinition(
                SenderLinesSql.Insert,
                new
                {
                    request.ProviderId,
                    request.LineNumber,
                    request.IsSharedLine,
                    request.CustomerId,
                    request.ProviderAccountId,
                    request.IsActive,
                },
                cancellationToken: cancellationToken));

            if (id is null)
                return Error.Validation("sender_lines.unknown_reference", UserMessages.ReferenceData.SenderLineUnknownReference);

            return new CreateSenderLineResponse(id.Value);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict("FK_SenderLine_Provider"))
        {
            return Error.Validation("sender_lines.unknown_provider", UserMessages.ReferenceData.SenderLineUnknownProvider);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict("FK_SenderLine_Customer"))
        {
            return Error.Validation("sender_lines.unknown_customer", UserMessages.ReferenceData.SenderLineUnknownCustomer);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict("FK_SenderLine_ProviderAccount"))
        {
            return Error.Validation("sender_lines.unknown_provider_account", UserMessages.ReferenceData.SenderLineUnknownProviderAccount);
        }
        catch (SqlException ex) when (ex.IsConstraintConflict())
        {
            return Error.Validation("sender_lines.unknown_reference", UserMessages.ReferenceData.SenderLineUnknownReference);
        }
    }
}
