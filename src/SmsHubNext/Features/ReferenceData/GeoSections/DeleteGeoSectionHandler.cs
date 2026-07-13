using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.GeoSections;

public sealed class DeleteGeoSectionHandler
{
    private readonly Db _db;

    public DeleteGeoSectionHandler(Db db) => _db = db;

    public async Task<Result> Handle(int id, int deletedByApiKeyId, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("geo.invalid_id", UserMessages.ReferenceData.InvalidGeoSection);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        using SqlTransaction transaction = connection.BeginTransaction();

        int? existingId = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            GeoSectionsSql.GetForDelete,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));

        if (existingId is null)
            return Error.NotFound("geo.not_found", UserMessages.ReferenceData.GeoSectionNotFound);

        bool hasActiveChildren = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            GeoSectionsSql.HasActiveChildren,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));

        if (hasActiveChildren)
            return Error.Conflict("geo.has_active_children", UserMessages.ReferenceData.GeoSectionHasActiveChildren);

        await connection.ExecuteAsync(new CommandDefinition(
            GeoSectionsSql.SoftDelete,
            new { Id = id, DeletedByApiKeyId = deletedByApiKeyId },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return Result.Success();
    }
}
