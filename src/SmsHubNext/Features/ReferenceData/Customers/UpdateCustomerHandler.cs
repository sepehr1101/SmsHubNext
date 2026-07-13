using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Customers;

public sealed class UpdateCustomerHandler
{
    private readonly Db _db;

    public UpdateCustomerHandler(Db db) => _db = db;

    public async Task<Result> Handle(
        short id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("customers.invalid_id", UserMessages.ReferenceData.InvalidCustomer);

        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        try
        {
            int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                CustomersSql.Update,
                new { Id = id, request.Name, request.Code, request.IsActive },
                cancellationToken: cancellationToken));

            return affectedRows == 0
                ? Error.NotFound("customers.not_found", UserMessages.ReferenceData.CustomerNotFound)
                : Result.Success();
        }
        catch (SqlException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("customers.code_exists", UserMessages.ReferenceData.CustomerCodeExists);
        }
    }
}
