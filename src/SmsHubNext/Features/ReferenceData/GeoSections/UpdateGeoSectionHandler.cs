using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.GeoSections;

public sealed class UpdateGeoSectionHandler
{
    private readonly Db _db;

    public UpdateGeoSectionHandler(Db db) => _db = db;

    public async Task<Result> Handle(int id, UpdateGeoSectionRequest request, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("geo.invalid_id", UserMessages.ReferenceData.InvalidGeoSection);

        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        int affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            GeoSectionsSql.Update,
            new { Id = id, request.Name, request.Code, request.IsActive },
            cancellationToken: cancellationToken));

        return affectedRows == 0
            ? Error.NotFound("geo.not_found", UserMessages.ReferenceData.GeoSectionNotFound)
            : Result.Success();
    }
}
