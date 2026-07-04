using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.GeoSections;

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
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        using SqlTransaction transaction = connection.BeginTransaction();

        string parentPath = "/";
        if (request.ParentGeoSectionId is int parentId)
        {
            string? path = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                GeoSectionsSql.GetPath, new { Id = parentId }, transaction, cancellationToken: cancellationToken));

            // Early return disposes the transaction, which rolls it back.
            if (path is null)
                return Error.Validation("geo.unknown_parent", UserMessages.ReferenceData.GeoUnknownParent);

            parentPath = path;
        }

        int id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            GeoSectionsSql.Insert,
            new { request.ParentGeoSectionId, SectionType = (byte)request.SectionType, request.Name, request.Code },
            transaction,
            cancellationToken: cancellationToken));

        string newPath = $"{parentPath}{id}/";
        await connection.ExecuteAsync(new CommandDefinition(
            GeoSectionsSql.UpdatePath, new { Id = id, Path = newPath }, transaction, cancellationToken: cancellationToken));

        transaction.Commit();
        return new CreateGeoSectionResponse(id, newPath);
    }
}
