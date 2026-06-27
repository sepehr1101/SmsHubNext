using Dapper;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

/// <summary>
/// Creates a geographic section and materializes its <c>Path</c> (README §4.7).
/// The path includes the new row's own id, so it is inserted then patched — both
/// within one transaction so the row is never visible without a path.
/// </summary>
public sealed class CreateGeoSectionHandler
{
    private readonly Db _db;

    public CreateGeoSectionHandler(Db db) => _db = db;

    public async Task<Result<CreateGeoSectionResponse>> Handle(
        CreateGeoSectionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using var connection = await _db.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var parentPath = "/";
        if (request.ParentGeoSectionId is int parentId)
        {
            var path = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                GeoSectionsSql.GetPath, new { Id = parentId }, transaction, cancellationToken: cancellationToken));

            // Early return disposes the transaction, which rolls it back.
            if (path is null)
                return Error.Validation("geo.unknown_parent", "The parent section does not exist.");

            parentPath = path;
        }

        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            GeoSectionsSql.Insert,
            new { request.ParentGeoSectionId, SectionType = (byte)request.SectionType, request.Name, request.Code },
            transaction,
            cancellationToken: cancellationToken));

        var newPath = $"{parentPath}{id}/";
        await connection.ExecuteAsync(new CommandDefinition(
            GeoSectionsSql.UpdatePath, new { Id = id, Path = newPath }, transaction, cancellationToken: cancellationToken));

        transaction.Commit();
        return new CreateGeoSectionResponse(id, newPath);
    }
}
