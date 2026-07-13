using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Providers;

public sealed class UpdateProviderHandler
{
    private readonly Db _db;

    public UpdateProviderHandler(Db db) => _db = db;

    public async Task<Result> Handle(byte id, UpdateProviderRequest request, CancellationToken cancellationToken)
    {
        if (id == 0)
            return Error.Validation("providers.invalid_id", UserMessages.ReferenceData.InvalidProvider);

        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            ProvidersSql.Update,
            new { Id = id, request.Name, request.BaseUrl, request.FallbackBaseUrl, request.IsActive },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("providers.not_found", UserMessages.ReferenceData.ProviderNotFound)
            : Result.Success();
    }
}
