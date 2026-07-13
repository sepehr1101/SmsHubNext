using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Customers;

public sealed class DeleteCustomerHandler
{
    private readonly Db _db;

    public DeleteCustomerHandler(Db db) => _db = db;

    public async Task<Result> Handle(short id, int deletedByApiKeyId, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("customers.invalid_id", UserMessages.ReferenceData.InvalidCustomer);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            CustomersSql.SoftDelete,
            new { Id = id, DeletedByApiKeyId = deletedByApiKeyId },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("customers.not_found", UserMessages.ReferenceData.CustomerNotFound)
            : Result.Success();
    }
}
