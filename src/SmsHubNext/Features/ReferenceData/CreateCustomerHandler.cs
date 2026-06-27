using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

public sealed class CreateCustomerHandler
{
    private readonly Db _db;

    public CreateCustomerHandler(Db db) => _db = db;

    public async Task<Result<CreateCustomerResponse>> Handle(
        CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        try
        {
            var id = await connection.ExecuteScalarAsync<short>(new CommandDefinition(
                CustomersSql.Insert,
                new { request.Name, request.Code },
                cancellationToken: cancellationToken));

            return new CreateCustomerResponse(id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) // unique index / key violation
        {
            return Error.Conflict("customers.code_exists", "A customer with this code already exists.");
        }
    }
}
