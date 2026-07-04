using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Customers;

public sealed class CreateCustomerHandler
{
    private readonly Db _db;

    public CreateCustomerHandler(Db db) => _db = db;

    public async Task<Result<CreateCustomerResponse>> Handle(
        CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        try
        {
            short id = await connection.ExecuteScalarAsync<short>(new CommandDefinition(
                CustomersSql.Insert,
                new { request.Name, request.Code },
                cancellationToken: cancellationToken));

            return new CreateCustomerResponse(id);
        }
        catch (SqlException ex) when (ex.IsUniqueViolation()) // duplicate Code
        {
            return Error.Conflict("customers.code_exists", UserMessages.ReferenceData.CustomerCodeExists);
        }
    }
}
