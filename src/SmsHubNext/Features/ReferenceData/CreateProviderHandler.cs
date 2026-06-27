using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

public sealed class CreateProviderHandler
{
    private readonly Db _db;

    public CreateProviderHandler(Db db) => _db = db;

    public async Task<Result<CreateProviderResponse>> Handle(
        CreateProviderRequest request,
        CancellationToken cancellationToken)
    {
        var validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        try
        {
            var id = await connection.ExecuteScalarAsync<byte>(new CommandDefinition(
                ProvidersSql.Insert,
                new { request.Name, request.Code, request.BaseUrl, request.FallbackBaseUrl },
                cancellationToken: cancellationToken));

            return new CreateProviderResponse(id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) // unique Code violation
        {
            return Error.Conflict("providers.code_exists", "A provider with this code already exists.");
        }
    }
}
